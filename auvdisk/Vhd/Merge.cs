using DiskAccessLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;

namespace auvdisk.Vhd
{
    internal static class Merge
    {
        public static void PerformMerge(string parent, string child, string target, Action<string> logger)
        {
            // TODO: I don't like a lot of returns here. It is easy to miss one when adding a new check
            
            parent = Path.GetFullPath(parent);
            child = Path.GetFullPath(child);
            target = Path.GetFullPath(target);

            if (!File.Exists(parent))
            {
                logger($"ERROR: {parent} does not exist");
                return;
            }
            else if (!File.Exists(child))
            {
                logger($"ERROR: {child} does not exist");
                return;
            }

            using (var childDisk = new DiscUtils.Vhd.Disk(child, FileAccess.Read))
            {
                var layers = childDisk.Layers.ToList();

                if (layers.Count != 2)
                {
                    logger($"ERROR: target image should consist of exactly 2 layers (fixed parent and sparse child)");
                    return;
                }

                var childLayer = layers[0];
                var parentLayer = layers[1];

                if (parentLayer.IsSparse || parentLayer.NeedsParent || !childLayer.IsSparse || !childLayer.NeedsParent)
                {
                    logger($"ERROR: invalid image layer configuration. Parent must be fixed, child must be sparse.");
                    return;
                }
                else if (Path.GetFullPath(parentLayer.FullPath) != parent)
                {
                    logger($"ERROR: child image points to {parentLayer.FullPath} while {parent} was passed");
                    return;
                }
            }

            if (parent != target && File.Exists(target))
            {
                logger($"ERROR: target image {target} already exists");
                return;
            }
            
            if (parent != target && !File.Exists(target) && !AnsiConsole.Confirm(
                    "Passed target is different than the parent; full image copy is needed before merge. This might take a while, proceed?"))
            {
                logger("Exiting");
                return;
            }
            else if (parent != target)
            {
                logger("Copying parent to target...");
                File.Copy(parent, target);
            }

            if (parent == target && !AnsiConsole.Confirm("Target and parent are the same file, are you sure to merge child directly into parent?"))
            {
                logger("Exiting");
                return;
            }

            var timer = new System.Diagnostics.Stopwatch();
            
            timer.Start();

            var diffHandler = new DifferencingVhdHandler(child);

            logger("Merging...");
            var foundSectors = diffHandler.MergeChangedSectorsIntoFixedParent(new FileStream(target, FileMode.Open, FileAccess.Write));

            timer.Stop();

            logger($"Moved {foundSectors} sectors from child image to parent");
            logger($"Merge took {timer.ElapsedMilliseconds}ms");

            if (parent == target)
            {
                logger(
                    $"As parent image was modified it's probably a good idea to delete all child images now, as they are effectively invalidated");
            }
            else
            {
                logger("New fixed merged image created.");
            }
        }
    }
}
