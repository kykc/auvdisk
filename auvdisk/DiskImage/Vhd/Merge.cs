using Spectre.Console;

namespace auvdisk.DiskImage.Vhd
{
    public static class Merge
    {
        public static void PerformMerge(string parent, string child, string target, Log.ILog logger, bool confirm = false)
        {
            // TODO: I don't like a lot of returns here. It is easy to miss one when adding a new check

            if (!File.Exists(parent))
            {
                logger.Error($"{parent} does not exist");
                return;
            }
            else if (!File.Exists(child))
            {
                logger.Error($"{child} does not exist");
                return;
            }
            
            using (var childDisk = Vhd.Util.OpenDiskWithDu(child, logger))
            {
                if (childDisk == null)
                {
                    // Logger already has all the details
                    return;
                }

                var layers = childDisk.Layers.ToList();

                if (layers.Count != 2)
                {
                    logger.Error($"Target image should consist of exactly 2 layers (fixed parent and sparse child)");
                    return;
                }

                var childLayer = layers[0];
                var parentLayer = layers[1];
                
                var passedParentId = Util.ReadVhdFooterSafe(parent)?.UniqueId;
                var detectedParentId = Util.ReadVhdFooterSafe(parentLayer.FullPath)?.UniqueId;

                if (parentLayer.IsSparse || parentLayer.NeedsParent || !childLayer.IsSparse || !childLayer.NeedsParent)
                {
                    logger.Error($"Invalid image layer configuration. Parent must be fixed, child must be sparse.");
                    return;
                }
                else if (passedParentId == null || detectedParentId == null)
                {
                    logger.Error($"Failed to obtain parent image unique id");
                    return;
                }
                else if (passedParentId != detectedParentId)
                {
                    logger.Error($"Child image points to {detectedParentId} while {passedParentId} was passed");
                    return;
                }
            }

            if (parent != target && File.Exists(target))
            {
                logger.Error($"Target image {target} already exists");
                return;
            }
            
            if (parent != target && !File.Exists(target) && !confirm && !AnsiConsole.Confirm(
                    "Passed target is different than the parent; full image copy is needed before merge. This might take a while, proceed?"))
            {
                logger.Log("Exiting");
                return;
            }
            else if (parent != target)
            {
                logger.Log("Copying parent to target...");
                File.Copy(parent, target);
            }

            if (parent == target && !AnsiConsole.Confirm("Target and parent are the same file, are you sure to merge child directly into parent?"))
            {
                logger.Log("Exiting");
                return;
            }

            var timer = new System.Diagnostics.Stopwatch();
            
            timer.Start();

            var diffHandler = new DifferencingVhdHandler(child);

            logger.Log("Merging...");
            using var fileStream = new FileStream(target, FileMode.Open, FileAccess.Write);
            var foundSectors = diffHandler.MergeChangedSectorsIntoFixedParent(fileStream);

            timer.Stop();

            logger.Log($"Moved {foundSectors} sectors from child image to parent");
            logger.Log($"Merge took {timer.ElapsedMilliseconds}ms");

            if (parent == target)
            {
                logger.Log(
                    $"As parent image was modified it's probably a good idea to delete all child images now, as they are effectively invalidated");
            }
            else
            {
                logger.Log("New fixed merged image created.");
            }
        }
    }
}
