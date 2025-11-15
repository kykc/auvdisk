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
using auvdisk.Bytes;
using auvdisk.Log;
using DiscUtils.Streams;
using Spectre.Console;

namespace auvdisk.DiskImage
{
    public static class DiskImageConverter
    {
        // TODO: make an option to disable prepending of EFI boot partition. For CreateBootableVhdLayout just support zero efiBootSize?
        // TODO: sanitize Flow results handling and types. Look at the ConvertVhdToFixedVhdx or ConvertLoopToVhd as an example
        public static Flow<DiskProbe.ProbeResult> ConvertLoopToVhd(string source, string target, Log.ILog logger, bool verbose, bool zeroFill = false)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                var sourceLength = new System.IO.FileInfo(source).Length;
                ulong efiBootSize = 512 * 1024 * 1024; // 512MiB

                logger.Log("Source file length is " + sourceLength.ToString());

                var createLayoutResult = Util.CreateBootableLayout(target, efiBootSize, (ulong)sourceLength, logger, zeroFill);

                if (createLayoutResult.IsError())
                {
                    return createLayoutResult.Map((_) => probeResult);
                }

                logger.Log("Opening VHD using DiscUtils");
                // Safe to use DiscUtils constructor as target is guaranteed to be of type fixed
                using (var disk = new DiscUtils.Vhd.Disk(target, FileAccess.ReadWrite))
                {
                    logger.Log("Formatting EFI boot partition into FAT32 and creating EFI/Boot directory");
                    var fat = FatFileSystem.FormatPartition(disk, 0, "Boot");
                    fat.CreateDirectory(@"EFI");
                    fat.CreateDirectory(@"EFI\Boot");

                    logger.Log("Copying data from loop to VHD. Depending on disk speed and image size this might take a while");
                    using (var sourceStream = File.OpenRead(source).WithProgress())
                    using (var targetStream = disk.Partitions[1].Open())
                    {
                        sourceStream.CopyTo(targetStream, logger);
                    }
                }

                logger.Log("It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");

                return Flow<DiskProbe.ProbeResult>.Ok(probeResult, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedFsType("", source, verbose) // this will effectively check that filesystem was recognized and will accept any type of FS
                .WithCheckedTargetAvailable(target)
                .Bind(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToLoop(string source, string target, Log.ILog logger, bool verbose, int partIdx = -1)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                logger.Log("Opening VHD using DiscUtils");

                var diskResult = Vhd.Util.OpenDiskWithDu(source, logger);

                if (diskResult.IsError())
                {
                    return Flows.Err<DiskProbe.ProbeResult>(diskResult.UnwrapErr(), logger);
                }
                
                using (var disk = diskResult.Unwrap())
                {
                    var dynamicOrDifferencing =
                        disk.Layers.Any((l) => l.IsSparse || l.NeedsParent) || disk.Layers.Count() > 1;
                    
                    if (dynamicOrDifferencing)
                    {
                        if (Program.IsInteractive && !AnsiConsole.Confirm(
                                "[yellow]WARNING: Source VHD is differencing or dynamic disk, this was never properly tested, proceed?[/]"))
                        {
                            return Flows.Err<DiskProbe.ProbeResult>("Cancelled by user", logger);
                        }
                        else
                        {
                            logger.Warning("Source VHD is differencing or dynamic disk, this was never properly tested");
                        }
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
                        return Flows.Err<DiskProbe.ProbeResult>("Partition not found", logger);
                    }

                    var selectedPart = parts.First();

                    logger.Log($"Selecting partition {selectedPart.idx}");
                    logger.Log("Opening partition using DiscUtils, target file using FileStream");

                    using (var partStream = disk.Partitions.Partitions[selectedPart.idx].Open().WithProgress())
                    using (var targetStream = new FileStream(target, FileMode.CreateNew))
                    {
                        logger.Log("Copying data from VHD to loop. Depending on disk speed and image size this might take a while");
                        partStream.CopyTo(targetStream, logger);
                    }
                }

                logger.Log("It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");

                return Flows.Ok(probeResult, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedTargetAvailable(target)
                .Bind(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToVhdx(string source, string target, ILog logger,
            bool verbose, bool fixedVhdx, bool forceZeroFill)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhd = VirtualDisk.OpenDisk(source, "vhd", FileAccess.Read, "", "")!;

                var vhdxResult = fixedVhdx
                    ? Vhdx.Util.CreateFixed(target, (ulong)vhd.Capacity, logger, forceZeroFill)
                    : Vhdx.Util.CreateDynamic(target, (ulong)vhd.Capacity, logger);

                if (vhdxResult.IsError())
                {
                    return Flows.Err<DiskProbe.ProbeResult>("Failed to create VHDx", logger);
                }

                using var vhdx = vhdxResult.Unwrap();
                
                vhd.Content.WithProgress().CopyTo(vhdx.Content, logger);
                vhdx.Content.Flush();

                return Flows.Ok(probeResult, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedTargetAvailable(target)
                .Bind(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdxToVhd(string source, string target, ILog logger,
            bool verbose, bool fixedVhd, bool forceZeroFill)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhdx = VirtualDisk.OpenDisk(source, "vhdx", FileAccess.Read, "", "");
                
                var createResult = fixedVhd 
                    ? Vhd.Util.CreateFixedVhd(target, (ulong)vhdx.Capacity, logger, forceZeroFill)
                    : Vhd.Util.CreateDynamicVhd(target, (ulong)vhdx.Capacity, logger);

                if (createResult.IsError())
                {
                    return Flows.Err<DiskProbe.ProbeResult>(createResult.UnwrapErr(), logger);
                }

                using var vhd = VirtualDisk.OpenDisk(target, "vhd", FileAccess.ReadWrite, "", "");

                vhdx.Content.WithProgress().CopyTo(vhd.Content, logger);
                vhd.Content.Flush();

                return Flows.Ok(probeResult, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHDX", source, verbose)
                .WithCheckedTargetAvailable(target)
                .Bind(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertQcow2ToRaw(string source, string target, ILog logger,
            bool verbose)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                logger.Log("Opening source as a qcow2 image");
                using var fs = File.OpenRead(source);
                var qcow2Stream = new Qcow2Stream(fs).WithProgress();
                logger.Log("Preparing target for writing");
                using var targetStream = File.Open(target, FileMode.CreateNew, FileAccess.ReadWrite);
                logger.Log("Copying data, this might take a while depending on disk speed and image size...");

                qcow2Stream.CopyTo(targetStream, logger); 
                targetStream.Flush();

                return probeResult;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("qcow2", source, verbose)
                .WithCheckedTargetAvailable(target)
                .Map(action);
        }

        public static Flow<DiskProbe.ProbeResult> ConvertImgToVhd(string source, Log.ILog logger, bool verbose)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
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

                return probeResult;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("RAW", source, verbose)
                .Map(action);

        }

        public static Flow<DiskProbe.ProbeResult> ConvertVhdToImg(string source, Log.ILog logger, bool verbose)
        {
            var action = (DiskProbe.ProbeResult probeResult) =>
            {
                logger.Log("Opening disk image using FileStream");

                using (var disk = new FileStream(source, FileMode.Open))
                {
                    logger.Log($"Truncating last {Vhd.Util.LbaSize.ToString()} bytes of the file");
                    long currentSize = disk.Length;
                    disk.SetLength(currentSize - (long)Vhd.Util.LbaSize); // Truncate VHD footer
                }

                logger.Log("Done! It's probably a good idea to rename file to *.img or something similar now");

                return probeResult;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(source)
                .WithCheckedDiskType("VHD", source, verbose)
                .WithCheckedVhdType(source, VirtualHardDiskType.Fixed)
                .Map(action);
        }
    }
}
