using auvdisk.Bytes;
using Spectre.Console;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;

namespace auvdisk.DiskImage.Vhd
{
    public static class Merge
    {
        private record DiskLayerModel(string FullPath, System.Guid? UniqueId, bool IsSparse, bool NeedsParent);
        private record DiskMergeModel(DiskLayerModel Parent, DiskLayerModel Child, System.Guid? PassedParentId);
        
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
                .Check(CheckHasTwoLayers,
                    (_) => "Target image should consist of exactly 2 layers (parent and child)")
                .Map(MakeModel)
                .Check(CheckNonDiffParentAndSingleChild,
                    (_) => "Invalid image layer configuration. Parent must be fixed or dynamic, child must be differencing")
                .Check(CheckParentIdsArePresent, (_) => "Failed to obtain parent image unique id")
                .Check(CheckParentIdsAreEqual,
                    (model) => $"Child image points to {model.Parent.UniqueId} while {model.PassedParentId} was passed")
                .Check(CheckTargetAvailable, (_) => $"Target image {target} already exists")
                .Check(ConfirmImageCopyIfNeeded, (_) => "Exiting")
                .CheckDiscardIf(_ => !inPlaceMode, DoImageCopy)
                .Check(ConfirmMergeIntoParentIfNeeded, (_) => "Exiting")
                .Bind(PerformMergeAction);
            
            Flow<DiscUtils.VirtualDisk> PerformMergeAction(DiskMergeModel _)
            {
                var timer = new System.Diagnostics.Stopwatch();

                timer.Start();

                var diffHandler = new DifferencingVhdHandler(child);
                
                var targetDisk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                
                ulong foundSectors = 0;
                logger.Log("Merging...");
                
                foundSectors = diffHandler.MergeChangedSectorsIntoParent(targetDisk.Content, logger);
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
            
            DiskMergeModel MakeModel(List<DiskLayerModel> layers)
            {
                return new(layers.Skip(1).First(), layers.First(), Util.ReadVhdFooterSafe(parent)?.UniqueId);
            }

            DiskLayerModel LayerToModel(VirtualDiskLayer layer) =>
                new(layer.FullPath, Util.ReadVhdFooterSafe(layer.FullPath)?.UniqueId, layer.IsSparse, layer.NeedsParent);

            bool CheckHasTwoLayers(List<DiskLayerModel> layers) => layers.Count() == 2;

            bool CheckNonDiffParentAndSingleChild(DiskMergeModel layers) =>
                layers.Parent is { NeedsParent: false } && layers.Child is { IsSparse: true, NeedsParent: true };

            bool CheckParentIdsArePresent(DiskMergeModel layers) =>
                layers.Parent.UniqueId != null && layers.PassedParentId != null;

            bool CheckParentIdsAreEqual(DiskMergeModel layers) =>
                layers.Parent.UniqueId == layers.PassedParentId;

            bool CheckTargetAvailable(DiskMergeModel layers) =>
                !File.Exists(target) || inPlaceMode;

            bool ConfirmImageCopyIfNeeded(DiskMergeModel layers)
            {
                const string msg =
                    "Passed target is different than the parent; full image copy is needed before merge. This might take a while, proceed?";
                return inPlaceMode || !Program.IsInteractive || AnsiConsole.Confirm(msg);
            }

            Flow<DiskMergeModel> DoImageCopy(DiskMergeModel model)
            {
                logger.Log("Copying parent to target...");
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                using var sourceDisk = VirtualDisk.OpenDisk(parent, FileAccess.Read);

                var dynamicParent = model.Parent.IsSparse;
                
                var targetDiskResult = dynamicParent 
                    ? Vhd.Util.CreateDynamicVhd(target, (ulong)sourceDisk.Capacity, logger)
                    : Vhd.Util.CreateFixedVhd(target, (ulong)sourceDisk.Capacity, logger, zeroFill);

                // Even though this leads to the return point in the middle, I don't think splitting this into two CheckDiscardIf
                // calls is a good idea.
                if (targetDiskResult.IsErr)
                {
                    return new(targetDiskResult.UnwrapErr());
                }

                using var targetDisk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                
                DiskImage.Util.LazyCopyDiskContents(sourceDisk, targetDisk, logger);
                
                if (!Program.IsInteractive)
                {
                    logger.Log($"Done copying parent to target in {timer.ElapsedMilliseconds}ms");
                }

                return Flows.Val(model);
            }

            bool ConfirmMergeIntoParentIfNeeded(DiskMergeModel layers)
            {
                const string msg = "Target and parent are the same file, are you sure to merge child directly into parent?";

                return !inPlaceMode || !Program.IsInteractive || AnsiConsole.Confirm(msg);
            }
        }
    }
}
