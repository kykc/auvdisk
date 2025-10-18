using auvdisk.Extensions;
using DiscUtils;
using DiscUtils.Fat;
using DiskAccessLibrary;
using DiskAccessLibrary.VHD;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using Spectre.Console;

namespace auvdisk.Convert
{
    internal class DiskImageConverter
    {
        // TODO: make an option to disable prepending of EFI boot partition
        public static void ConvertLoopToVhd(string source, string target, Action<String> logger, bool verbose, bool zeroFill = false)
        {
            var action = () =>
            {
                var sourceLength = new System.IO.FileInfo(source).Length;
                ulong efiBootSize = 512 * 1024 * 1024; // 512MiB

                logger("Source file length is " + sourceLength.ToString());

                Vhd.Util.CreateBootableFixedVhdLayout(target, efiBootSize, (ulong)sourceLength, logger, zeroFill);

                logger("Opening VHD using DiscUtils");
                using (var disk = new DiscUtils.Vhd.Disk(target, FileAccess.ReadWrite))
                {
                    logger("Formatting EFI boot partition into FAT32 and creating EFI/Boot directory");
                    var fat = FatFileSystem.FormatPartition(disk, 0, "Boot");
                    fat.CreateDirectory(@"EFI");
                    fat.CreateDirectory(@"EFI\Boot");

                    logger("Copying data from loop to VHD. Depending on disk speed and image size this might take a while");
                    using (var sourceStream = File.OpenRead(source))
                    using (var targetStream = disk.Partitions[1].Open())
                    {
                        sourceStream.CopyTo(targetStream);
                    }
                }

                logger("Closed VHD, done! It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");
            };

            action
                .WithCheckedTargetAvailable(target, logger)
                .WithCheckedFsType("", source, logger, verbose) // this will effectively check that filesystem was recognized and will accept any type of FS
                .WithCheckedSourceExists(source, logger)();
        }

        public static void ConvertVhdToLoop(string source, string target, Action<string> logger, bool verbose, int partIdx = -1)
        {
            var action = () =>
            {
                logger("Opening VHD using DiscUtils");

                using (var disk = new DiscUtils.Vhd.Disk(source, FileAccess.Read))
                {
                    var dynamicOrDifferencing =
                        disk.Layers.Any((l) => l.IsSparse || l.NeedsParent) || disk.Layers.Count() > 1;
                    
                    if (dynamicOrDifferencing && !AnsiConsole.Confirm(
                            "[yellow]WARNING: Source VHD is differencing or dynamic disk, this was never properly tested, proceed?[/]"))
                    {
                        return;
                    }
                    
                    logger("VHD contains " + disk.Partitions.Count + " partitions:");
                    var parts = disk.Partitions.Partitions.Select((part, idx) =>
                    {
                        logger($"Partition {idx} containing {part.SectorCount} LBA Sectors [{part.FirstSector}-{part.LastSector}]");
                        return (idx, part.SectorCount);
                    }).ToList();

                    parts = partIdx >= 0 
                        ? parts.Where(p => p.idx == partIdx).ToList() // find partition by provided index 
                        : parts.OrderByDescending((x) => x.SectorCount).ToList(); // select largest partition by default

                    if (parts.Count == 0)
                    {
                        logger($"ERROR: partition not found");
                        return;
                    }

                    var selectedPart = parts.First();

                    logger($"Selecting partition {selectedPart.idx}");
                    logger("Opening partition using DiscUtils, target file using FileStream");

                    using (var partStream = disk.Partitions.Partitions[selectedPart.idx].Open())
                    using (var targetStream = new FileStream(target, FileMode.Create))
                    {
                        logger("Copying data from VHD to loop. Depending on disk speed and image size this might take a while");
                        partStream.CopyTo(targetStream);
                    }
                }

                logger("Done! It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");
            };

            action
                .WithCheckedTargetAvailable(target, logger)
                .WithCheckedDiskType("VHD", source, logger, verbose)
                .WithCheckedSourceExists(source, logger)();
        }

        public static void ConvertImgToVhd(string source, Action<string> logger, bool verbose)
        {
            var action = () =>
            {
                logger("Opening disk image using FileStream");
                using (var disk = new FileStream(source, FileMode.Open))
                {
                    long diskSize = disk.Length;

                    logger("Generating VHD footer using DiskAccessLibrary");
                    VHDFooter vhdFooter = new VHDFooter();
                    vhdFooter.OriginalSize = (ulong)diskSize;
                    vhdFooter.CurrentSize = (ulong)diskSize;
                    vhdFooter.SetCurrentTimeStamp();
                    vhdFooter.SetDiskGeometry((ulong)diskSize / Program.LbaSize);

                    logger("Appending VHD footer to image file");
                    disk.Seek(0, SeekOrigin.End);
                    foreach (var bt in vhdFooter.GetBytes())
                    {
                        disk.WriteByte(bt);
                    }
                }

                logger("Done! It's probably a good idea to rename file to *.vhd now");
            };

            action
                .WithCheckedDiskType("RAW", source, logger, verbose)
                .WithCheckedSourceExists(source, logger)();
        }

        public static void ConvertVhdToImg(string source, Action<string> logger, bool verbose) // TODO: check that VHD is fixed
        {
            var action = () =>
            {
                logger("Opening disk image using FileStream");

                using (var disk = new FileStream(source, FileMode.Open))
                {
                    logger($"Truncating last {Program.LbaSize.ToString()} bytes of the file");
                    long currentSize = disk.Length;
                    disk.SetLength(currentSize - (long)Program.LbaSize); // Truncate VHD footer
                }

                logger("Done! It's probably a good idea to rename file to *.img or something similar now");
            };

            action
                .WithCheckedVhdType(source, VirtualHardDiskType.Fixed, logger)
                .WithCheckedDiskType("VHD", source, logger, verbose)
                .WithCheckedSourceExists(source, logger)();
        }

        
    }
}
