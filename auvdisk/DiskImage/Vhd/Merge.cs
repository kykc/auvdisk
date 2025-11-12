using auvdisk.Bytes;
using Spectre.Console;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using ShellProgressBar;
using DiskLayerModel = (string FullPath, System.Guid? UniqueId, bool IsSparse, bool NeedsParent);

namespace auvdisk.DiskImage.Vhd
{
    using DiskMergeModel = (DiskLayerModel Parent, DiskLayerModel Child, System.Guid? PassedParentId);
    using DiskMergeTask = (string Parent, string Child);

    public static class Merge
    {
        public static Flow<DiscUtils.Vhd.Disk> PerformMerge(string parent, string child, string target, Log.ILog logger)
        {
            bool skipInteractivity = !Program.IsInteractive;
            
            return Flow<Value<DiskMergeTask>>
                .Ok((parent, child).Some(), logger)
                .Check((t) => File.Exists(t.Val.Parent), (t) => $"{t.Val.Parent} does not exist")
                .Check((t) => File.Exists(t.Val.Child), (t) => $"{t.Val.Child} does not exist")
                .MapOr((t) => Util.OpenDiskWithDu(t.Val.Child, logger), "Failed to open child VHD disk")
                .MapDispose((disk) => disk.Layers.Select(LayerToModel).ToList())
                .Check(CheckHasTwoLayers,
                    (_) => "Target image should consist of exactly 2 layers (fixed parent and sparse child)")
                .Map<Value<DiskMergeModel>>((layers) => (layers[1], layers[0], Util.ReadVhdFooterSafe(parent)?.UniqueId).Some())
                .Check(CheckFixedParentAndSingleChild,
                    (_) => "Invalid image layer configuration. Parent must be fixed, child must be sparse")
                .Check(CheckParentIdsArePresent, (_) => "Failed to obtain parent image unique id")
                .Check(CheckParentIdsAreEqual,
                    (model) => $"Child image points to {model.Val.Parent.UniqueId} while {model.Val.PassedParentId} was passed")
                .Check(CheckTargetAvailable, (_) => $"Target image {target} already exists")
                .Check(ConfirmImageCopyIfNeeded, (_) => "Exiting")
                .WithSideEffect(DoImageCopyIfNeeded)
                .Check(ConfirmMergeIntoParentIfNeeded, (_) => "Exiting")
                .MapOr(PerformMergeAction, "Failed to open resulting disk image");

            DiscUtils.Vhd.Disk? PerformMergeAction(Value<DiskMergeModel> _)
            {
                var timer = new System.Diagnostics.Stopwatch();

                timer.Start();

                var diffHandler = new DifferencingVhdHandler(child);
                
                var fileStream = new FileStream(target, FileMode.Open, FileAccess.Write);
                
                ulong foundSectors = 0;
                logger.Log("Merging...");
                
                // Basically, is we're in the interactive mode we want to present progress bar
                foundSectors = diffHandler.MergeChangedSectorsIntoFixedParent(fileStream, skipInteractivity ? null : new DifferencingVhdHandler.ProgressOptions());

                timer.Stop();

                logger.Log($"Moved {foundSectors} sectors from child image to parent");

                // Interactive mode will have timing on the progress bar
                if (skipInteractivity)
                {
                    logger.Log($"Merge took {timer.ElapsedMilliseconds}ms");
                }

                const string inPlaceMsg = "As parent image was modified it's probably a good idea to delete all child images now, as they are effectively invalidated";
                const string newImgMsg = "New fixed merged image created.";

                logger.Log(parent == target ? inPlaceMsg : newImgMsg);
                fileStream.Dispose();

                return Util.OpenDiskWithDu(target, logger);
            };

            DiskLayerModel LayerToModel(VirtualDiskLayer layer) =>
                (layer.FullPath, Util.ReadVhdFooterSafe(layer.FullPath)?.UniqueId, layer.IsSparse, layer.NeedsParent);

            bool CheckHasTwoLayers(List<DiskLayerModel> layers) => layers.Count() == 2;

            bool CheckFixedParentAndSingleChild(Value<DiskMergeModel> layers) =>
                layers.Val.Parent is { IsSparse: false, NeedsParent: false } && layers.Val.Child is { IsSparse: true, NeedsParent: true };

            bool CheckParentIdsArePresent(Value<DiskMergeModel> layers) =>
                layers.Val.Parent.UniqueId != null && layers.Val.PassedParentId != null;

            bool CheckParentIdsAreEqual(Value<DiskMergeModel> layers) =>
                layers.Val.Parent.UniqueId == layers.Val.PassedParentId;

            bool CheckTargetAvailable(Value<DiskMergeModel> layers) =>
                !File.Exists(target) || parent == target; // TODO: parent and target paths might be "spelled" differently but point to the same file

            bool ConfirmImageCopyIfNeeded(Value<DiskMergeModel> layers)
            {
                const string msg =
                    "Passed target is different than the parent; full image copy is needed before merge. This might take a while, proceed?";
                return parent == target || skipInteractivity || AnsiConsole.Confirm(msg);
            }

            void DoImageCopyIfNeeded()
            {
                logger.Log("Copying parent to target...");
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                using var sourceStream = new FileStream(parent, FileMode.Open, FileAccess.Read).WithProgress();
                using var targetStream = File.OpenWrite(target);
                
                if (skipInteractivity)
                {
                    sourceStream.CopyTo(targetStream);
                    logger.Log($"Done copying parent to target in {timer.ElapsedMilliseconds}ms");
                }
                else
                {
                    sourceStream.CopyTo(targetStream, new StreamCopyProgressWrapper.ProgressOptions());
                }
            }

            bool ConfirmMergeIntoParentIfNeeded(Value<DiskMergeModel> layers)
            {
                const string msg = "Target and parent are the same file, are you sure to merge child directly into parent?";

                return parent != target || skipInteractivity || AnsiConsole.Confirm(msg);
            }
        }
    }
}
