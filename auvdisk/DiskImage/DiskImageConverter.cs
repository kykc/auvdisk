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
using auvdisk.Log;
using DotNext.Collections.Generic;
using Spectre.Console;

namespace auvdisk.DiskImage
{
    public static class DiskImageConverter
    {
        // TODO: make an option to disable prepending of EFI boot partition
        public static Flow<DiskProbe.ProbeResult> ConvertLoopToVhd(string source, string target, Log.ILog logger, bool verbose, bool zeroFill = false)
        {
            var action = () =>
            {
                var sourceLength = new System.IO.FileInfo(source).Length;
                ulong efiBootSize = 512 * 1024 * 1024; // 512MiB

                logger.Log("Source file length is " + sourceLength.ToString());

                Vhd.Util.CreateBootableFixedVhdLayout(target, efiBootSize, (ulong)sourceLength, logger, zeroFill);

                logger.Log("Opening VHD using DiscUtils");
                // Safe to use DiscUtils constructor as target is guaranteed to be of type fixed
                using (var disk = new DiscUtils.Vhd.Disk(target, FileAccess.ReadWrite))
                {
                    logger.Log("Formatting EFI boot partition into FAT32 and creating EFI/Boot directory");
                    var fat = FatFileSystem.FormatPartition(disk, 0, "Boot");
                    fat.CreateDirectory(@"EFI");
                    fat.CreateDirectory(@"EFI\Boot");

                    logger.Log("Copying data from loop to VHD. Depending on disk speed and image size this might take a while");
                    using (var sourceStream = File.OpenRead(source))
                    using (var targetStream = disk.Partitions[1].Open())
                    {
                        sourceStream.CopyTo(targetStream);
                    }
                }

                logger.Log("Closed VHD, done! It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedFsType("", source, verbose) // this will effectively check that filesystem was recognized and will accept any type of FS
                .WithCheckedTargetAvailable(target)
                .WithSideEffect(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToLoop(string source, string target, Log.ILog logger, bool verbose, int partIdx = -1)
        {
            var action = () =>
            {
                logger.Log("Opening VHD using DiscUtils");
                
                using (var disk = Vhd.Util.OpenDiskWithDu(source, logger))
                {
                    if (disk == null)
                    {
                        // Logger already has all the details
                        return;
                    }

                    var dynamicOrDifferencing =
                        disk.Layers.Any((l) => l.IsSparse || l.NeedsParent) || disk.Layers.Count() > 1;
                    
                    if (dynamicOrDifferencing && !AnsiConsole.Confirm(
                            "[yellow]WARNING: Source VHD is differencing or dynamic disk, this was never properly tested, proceed?[/]"))
                    {
                        return;
                    }
                    
                    logger.Log("VHD contains " + disk.Partitions.Count + " partitions:");
                    var parts = disk.Partitions.Partitions.Select((part, idx) =>
                    {
                        logger.Log($"Partition {idx} containing {part.SectorCount} LBA Sectors [{part.FirstSector}-{part.LastSector}]");
                        return (idx, part.SectorCount);
                    }).ToList();

                    parts = partIdx >= 0 
                        ? parts.Where(p => p.idx == partIdx).ToList() // find partition by provided index 
                        : parts.OrderByDescending((x) => x.SectorCount).ToList(); // select largest partition by default

                    if (parts.Count == 0)
                    {
                        logger.Error($"Partition not found");
                        return;
                    }

                    var selectedPart = parts.First();

                    logger.Log($"Selecting partition {selectedPart.idx}");
                    logger.Log("Opening partition using DiscUtils, target file using FileStream");

                    using (var partStream = disk.Partitions.Partitions[selectedPart.idx].Open())
                    using (var targetStream = new FileStream(target, FileMode.Create))
                    {
                        logger.Log("Copying data from VHD to loop. Depending on disk speed and image size this might take a while");
                        partStream.CopyTo(targetStream);
                    }
                }

                logger.Log("Done! It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedTargetAvailable(target)
                .WithSideEffect(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToFixedVhdx(string source, string target, ILog logger,
            bool verbose)
        {
            var action = () =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhd = VirtualDisk.OpenDisk(source, "vhd", FileAccess.Read, "", "")!;

                using var targetStream =
                    new FileStream(target, FileMode.Create, FileAccess.ReadWrite);
                using var vhdx = DiscUtils.Vhdx.Disk.InitializeFixed(targetStream, DiscUtils.Streams.Ownership.None, vhd.Capacity)!;

                vhd.Content.CopyTo(vhdx.Content);
                targetStream.Flush();
                logger.Log("Done.");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedTargetAvailable(target)
                .WithSideEffect(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdxToFixedVhd(string source, string target, ILog logger,
            bool verbose)
        {
            var action = () =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhdx = VirtualDisk.OpenDisk(source, "vhdx", FileAccess.Read, "", "");

                using var targetStream =
                    new FileStream(target, FileMode.Create, FileAccess.ReadWrite);
                using var vhd =
                    DiscUtils.Vhd.Disk.InitializeFixed(targetStream, DiscUtils.Streams.Ownership.None, vhdx.Capacity);

                vhdx.Content.CopyTo(vhd.Content);
                targetStream.Flush();
                logger.Log("Done.");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHDX", source, verbose)
                .WithCheckedTargetAvailable(target)
                .WithSideEffect(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertImgToVhd(string source, Log.ILog logger, bool verbose)
        {
            var action = () =>
            {
                logger.Log("Opening disk image using FileStream");
                using (var disk = new FileStream(source, FileMode.Open))
                {
                    long diskSize = disk.Length;

                    logger.Log("Generating VHD footer using DiskAccessLibrary");
                    var vhdFooter = Vhd.Util.CreateVhdFooter((ulong)diskSize);
                    
                    logger.Log("Appending VHD footer to image file");
                    disk.Seek(0, SeekOrigin.End);
                    foreach (var bt in vhdFooter.GetBytes())
                    {
                        disk.WriteByte(bt);
                    }
                }

                logger.Log("Done! It's probably a good idea to rename file to *.vhd now");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("RAW", source, verbose)
                .WithSideEffect(action);

        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToImg(string source, Log.ILog logger, bool verbose)
        {
            var action = () =>
            {
                logger.Log("Opening disk image using FileStream");

                using (var disk = new FileStream(source, FileMode.Open))
                {
                    logger.Log($"Truncating last {Vhd.Util.LbaSize.ToString()} bytes of the file");
                    long currentSize = disk.Length;
                    disk.SetLength(currentSize - (long)Vhd.Util.LbaSize); // Truncate VHD footer
                }

                logger.Log("Done! It's probably a good idea to rename file to *.img or something similar now");
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedVhdType(source, VirtualHardDiskType.Fixed)
                .WithSideEffect(action);
        }
    }
}
