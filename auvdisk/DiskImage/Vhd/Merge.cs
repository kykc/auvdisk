using auvdisk.Bytes;
using Spectre.Console;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using DiskAccessLibrary.VHD;

namespace auvdisk.DiskImage.Vhd
{
    public static class Merge
    {
        private record DiskLayerModel(string FullPath, System.Guid? UniqueId, bool IsSparse, bool NeedsParent);
        private record DiskMergeModel(DiskLayerModel Parent, DiskLayerModel Child, VHDFooter PassedParentFooter, List<DiskLayerModel> AllLayers);
        
        public static Flow<DiscUtils.VirtualDisk> PerformMerge(string parent, string child, string target, Log.ILog logger, bool zeroFill = false)
        {
            bool inPlaceMode = Path.GetFullPath(parent) == Path.GetFullPath(target);
            logger.Log($"In-place mode: {inPlaceMode}");
            
            return Flows
                .Val(new {parent, child})
                .Check((t) => File.Exists(t.parent), (t) => $"{t.parent} does not exist")
                .Check((t) => File.Exists(t.child), (t) => $"{t.child} does not exist")
                .Bind((t) => Util.OpenDiskWithDu(t.child, logger))
                .MapDispose((disk) => disk.Layers.Select(LayerToModel).ToList())
                .Bind(MakeModel)
                .Check(CheckParentChildRelation, _ => "Failed to evaluate parent-child relation")
                .Check(CheckParentIdsArePresent, (_) => "Failed to obtain parent image unique id")
                .Check(CheckParentIdsAreEqual,
                    (model) => $"Child image points to {model.Parent.UniqueId} while {model.PassedParentFooter.UniqueId} was passed")
                .CheckIf(_ => !inPlaceMode, CheckTargetAvailable, (_) => $"Target image {target} already exists")
                .CheckIf(_ => !inPlaceMode, ConfirmImageCopy, (_) => "Exiting")
                .CheckDiscardIf(_ => !inPlaceMode, PerformImageCopy)
                .CheckIf(_ => inPlaceMode, ConfirmMergeIntoParent, (_) => "Exiting")
                .Bind(PerformMergeAction);
            
            Flow<DiscUtils.VirtualDisk> PerformMergeAction(DiskMergeModel _)
            {
                var timer = new System.Diagnostics.Stopwatch();

                timer.Start();

                using var diffHandler = new DifferencingVhdHandler(child);
                
                var targetDisk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                
                logger.Log("Merging...");
                
                ulong foundSectors = diffHandler.MergeChangedSectorsIntoParent(targetDisk.Content, logger);
                targetDisk.Content.Flush();

                timer.Stop();

                logger.Log($"Moved {foundSectors} sectors from child image to parent");

                // Interactive mode will have timing on the progress bar
                if (!Program.IsInteractive)
                {
                    logger.Log($"Merge took {timer.ElapsedMilliseconds}ms");
                }

                const string inPlaceMsg = "As parent image was modified it's probably a good idea to delete all child images now, as they are effectively invalidated";
                const string newImgMsg = "New fixed merged image created.";

                logger.Log(inPlaceMode ? inPlaceMsg : newImgMsg);
                
                return Flows.Val(targetDisk);
            }
            
            Flow<DiskMergeModel> MakeModel(List<DiskLayerModel> layers)
            {
                var maybeFooter = Util.ReadVhdFooterSafe(parent);

                if (!maybeFooter.IsSome())
                {
                    return new("Failed to read VHD footer of the parent image");
                }

                var model = new DiskMergeModel(layers.Skip(1).First(), layers.First(), maybeFooter!, layers);
                return Flows.Val(model);
            }

            DiskLayerModel LayerToModel(VirtualDiskLayer layer) =>
                new(layer.FullPath, Util.ReadVhdFooterSafe(layer.FullPath)?.UniqueId, layer.IsSparse, layer.NeedsParent);

            bool CheckParentChildRelation(DiskMergeModel layers)
            {
                return Fs.Util.AreSameFile(child, layers.Child.FullPath) && Fs.Util.AreSameFile(parent, layers.Parent.FullPath);
            }

            bool CheckParentIdsArePresent(DiskMergeModel layers) =>
                layers.Parent.UniqueId != null;

            bool CheckParentIdsAreEqual(DiskMergeModel layers) =>
                layers.Parent.UniqueId == layers.PassedParentFooter.UniqueId;

            bool CheckTargetAvailable(DiskMergeModel layers) =>
                !File.Exists(target);

            bool ConfirmImageCopy(DiskMergeModel layers)
            {
                const string msg =
                    "Passed target is different than the parent; full image copy is needed before merge. This might take a while, proceed?";
                return !Program.IsInteractive || AnsiConsole.Confirm(msg);
            }

            Flow<DiskMergeModel> PerformImageCopy(DiskMergeModel outerModel)
            {
                logger.Log("Copying parent to target...");
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                return outerModel.Flow()
                    .CheckDiscard(model =>
                    {
                        return model.PassedParentFooter.DiskType switch
                        {
                            VirtualHardDiskType.Fixed => Vhd.Util.CreateFixedVhd(target, model.PassedParentFooter.CurrentSize, logger, zeroFill),
                            VirtualHardDiskType.Dynamic => Vhd.Util.CreateDynamicVhd(target, model.PassedParentFooter.CurrentSize, logger),
                            VirtualHardDiskType.Differencing => Vhd.Util.CreateDifferentialVhd(model.AllLayers.Skip(2).First().FullPath,
                                target, logger),
                            var otherType => new($"Unexpected disk type <{otherType}>")
                        };
                    })
                    .CheckDiscard(model =>
                    {
                        // NEVER use paths from parent locators here for the `target`
                        // we need to write to the file requested by the user
                        if (model.PassedParentFooter.DiskType == VirtualHardDiskType.Differencing)
                        {
                            using var diffHandler = new DifferencingVhdHandler(parent);
                            using var targetDisk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                            diffHandler.MergeChangedSectorsIntoParent(targetDisk.Content, logger);
                            return Flows.Val(None.Value);
                        }
                        else
                        {
                            using var sourceDisk = VirtualDisk.OpenDisk(parent, FileAccess.Read);
                            using var targetDisk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                            
                            return DiskImage.Util.LazyCopyDiskContents(sourceDisk, targetDisk, logger);
                        }
                    })
                    .WithSideEffect(_ =>
                    {
                        if (!Program.IsInteractive)
                        {
                            logger.Log($"Done copying parent to target in {timer.ElapsedMilliseconds}ms");
                        }
                    });
            }

            bool ConfirmMergeIntoParent(DiskMergeModel layers)
            {
                const string msg = "Target and parent are the same file, are you sure to merge child directly into parent?";

                return !Program.IsInteractive || AnsiConsole.Confirm(msg);
            }
        }
    }
}
