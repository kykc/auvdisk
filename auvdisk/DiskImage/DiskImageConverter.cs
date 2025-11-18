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
using auvdisk.DiskImage.Vhd;
using auvdisk.Log;
using DiscUtils.Streams;
using Spectre.Console;

namespace auvdisk.DiskImage
{
    public static class DiskImageConverter
    {
        public static Flow<VhdFileInfo> ConvertLoopToVhd(string source, string target, Log.ILog logger, bool verbose, bool zeroFill = false, bool noBoot = false)
        {
            var action = (None none) =>
            {
                var sourceLength = new System.IO.FileInfo(source).Length;
                ulong efiBootSize = noBoot ? 0UL : 512UL * 1024 * 1024; // 512MiB

                logger.Log("Source file length is " + sourceLength.ToString());

                var createLayoutResult = Util.CreateVdiskWithGptLayout(target, efiBootSize, (ulong)sourceLength, logger, zeroFill);

                if (createLayoutResult.IsError())
                {
                    return new(createLayoutResult.UnwrapErr(), logger);
                }

                logger.Log("Opening VHD using DiscUtils");
                // Safe to use DiscUtils constructor as target is guaranteed to be of type fixed
                using var disk = new DiscUtils.Vhd.Disk(target, FileAccess.ReadWrite);
                
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
                
                logger.Log("It might be a good idea to run `e2fsck -f` and `resize2fs` on the target");
                
                return Flows.Ok(new VhdFileInfo(disk, target, VirtualHardDiskType.Fixed), logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedFsType(_ => "", _ => source, _ => verbose) // this will effectively check that filesystem was recognized and will accept any type of FS
                .WithCheckedTargetAvailable(_ => target)
                .Bind(action);
        }

        public static Flow<None> ConvertVhdToLoop(string source, string target, Log.ILog logger, bool verbose, int partIdx = -1)
        {
            var action = (None none) =>
            {
                logger.Log("Opening VHD using DiscUtils");

                var diskResult = Vhd.Util.OpenDiskWithDu(source, logger);

                if (diskResult.IsError())
                {
                    return new(diskResult.UnwrapErr(), logger);
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
                            return new("Cancelled by user", logger);
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
                        return new("Partition not found", logger);
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

                return Flows.Ok(none, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "VHD", _ => source, _ => verbose)
                .WithCheckedTargetAvailable(_ => target)
                .Bind(action);
        }

        // TODO: use source .Content.Extents in case of dynamic target. Or even always?
        public static Flow<None> ConvertVhdToVhdx(string source, string target, ILog logger,
            bool verbose, bool? fixedVhdx, bool forceZeroFill)
        {
            var action = (None none) =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhd = VirtualDisk.OpenDisk(source, "vhd", FileAccess.Read, "", "")!;

                if (!fixedVhdx.HasValue)
                {
                    logger.Warning($"Target disk type/variant wasn't explicitly specified, choosing the same type as source disk: <{vhd.DiskTypeInfo.Variant}>");
                    fixedVhdx = vhd.DiskTypeInfo.Variant == "fixed";
                }

                var vhdxResult = fixedVhdx.Value
                    ? Vhdx.Util.CreateFixed(target, (ulong)vhd.Capacity, logger, forceZeroFill)
                    : Vhdx.Util.CreateDynamic(target, (ulong)vhd.Capacity, logger);

                if (vhdxResult.IsError())
                {
                    return new("Failed to create VHDx", logger);
                }

                using var vhdx = vhdxResult.Unwrap();
                
                // This is needed in order to efficiently handle dynamic source disk. Then it will copy only allocated parts.
                var toCopyLength = vhd.Content.Extents.Select(x => x.Length).Sum();
                var progressData = new StreamCopyProgressWrapper.ProgressData("Copying", toCopyLength);
                Utils.WithProgress(logger, progressData, progress =>
                {
                    foreach (var extent in vhd.Content.Extents)
                    {
                        var source = new SubStream(vhd.Content, extent.Start, extent.Length);
                        vhdx.Content.Seek(extent.Start, SeekOrigin.Begin);
                        StreamCopyProgressWrapper.CopyTo(source, vhdx.Content, logger, progressData, progress);
                    }

                    return progressData;
                });
                
                vhdx.Content.Flush();

                return Flows.Ok(none, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "VHD", _ => source, _ => verbose)
                .WithCheckedTargetAvailable(_ => target)
                .Bind(action);
        }

        // TODO: use source .Content.Extents in case of dynamic target. Or even always?
        public static Flow<VhdFileInfo> ConvertVhdxToVhd(string source, string target, ILog logger,
            bool verbose, bool? fixedVhd, bool forceZeroFill)
        {
            var action = (None none) =>
            {
                // DU is very brittle if VHD constructor is used directly, need to use factory
                // See auvdisk.test/Vhd/ParentLocatorTest for details
                using var vhdx = VirtualDisk.OpenDisk(source, "vhdx", FileAccess.Read, "", "");
                
                if (!fixedVhd.HasValue)
                {
                    logger.Warning($"Target disk type/variant wasn't explicitly specified, choosing the same type as source disk: <{vhdx.DiskTypeInfo.Variant}>");
                    fixedVhd = vhdx.DiskTypeInfo.Variant == "fixed";
                }
                
                var createResult = fixedVhd.Value
                    ? Vhd.Util.CreateFixedVhd(target, (ulong)vhdx.Capacity, logger, forceZeroFill)
                    : Vhd.Util.CreateDynamicVhd(target, (ulong)vhdx.Capacity, logger);

                if (createResult.IsError())
                {
                    return new(createResult.UnwrapErr(), logger);
                }

                using var vhd = VirtualDisk.OpenDisk(target, "vhd", FileAccess.ReadWrite, "", "");
                
                // This is needed in order to efficiently handle dynamic source disk. Then it will copy only allocated parts.
                var toCopyLength = vhdx.Content.Extents.Select(x => x.Length).Sum();
                var progressData = new StreamCopyProgressWrapper.ProgressData("Copying", toCopyLength);
                Utils.WithProgress(logger, progressData, progress =>
                {
                    foreach (var extent in vhdx.Content.Extents)
                    {
                        var source = new SubStream(vhdx.Content, extent.Start, extent.Length);
                        vhd.Content.Seek(extent.Start, SeekOrigin.Begin);
                        StreamCopyProgressWrapper.CopyTo(source, vhd.Content, logger, progressData, progress);
                    }

                    return progressData;
                });
                
                vhd.Content.Flush();

                return Flows.Ok(VhdFileInfo.Make(vhd, target, fixedVhd.Value ? VirtualHardDiskType.Fixed : VirtualHardDiskType.Dynamic)!, logger);
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "VHDX", _ => source, _ => verbose)
                .WithCheckedTargetAvailable(_ => target)
                .Bind(action);
        }

        public static Flow<None> ConvertQcow2ToRaw(string source, string target, ILog logger,
            bool verbose)
        {
            var action = (None none) =>
            {
                logger.Log("Opening source as a qcow2 image");
                using var fs = File.OpenRead(source);
                var qcow2Stream = new Qcow2Stream(fs).WithProgress();
                logger.Log("Preparing target for writing");
                using var targetStream = File.Open(target, FileMode.CreateNew, FileAccess.ReadWrite);
                logger.Log("Copying data, this might take a while depending on disk speed and image size...");

                qcow2Stream.CopyTo(targetStream, logger); 
                targetStream.Flush();

                return none;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "qcow2", _ => source, _ => verbose)
                .WithCheckedTargetAvailable(_ => target)
                .Map(action);
        }

        public static Flow<None> ConvertImgToVhd(string source, Log.ILog logger, bool verbose)
        {
            var action = (None none) =>
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

                return none;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "RAW", _ => source, _ => verbose)
                .Map(action);
        }

        public static Flow<None> ConvertVhdToImg(string source, Log.ILog logger, bool verbose)
        {
            var action = (None none) =>
            {
                logger.Log("Opening disk image using FileStream");

                using (var disk = new FileStream(source, FileMode.Open))
                {
                    logger.Log($"Truncating last {Vhd.Util.LbaSize.ToString()} bytes of the file");
                    long currentSize = disk.Length;
                    disk.SetLength(currentSize - (long)Vhd.Util.LbaSize); // Truncate VHD footer
                }

                logger.Log("Done! It's probably a good idea to rename file to *.img or something similar now");

                return none;
            };

            return Flow<None>.Ok(None.Value, logger)
                .WithCheckedSourceExists(_ => source)
                .WithCheckedDiskType(_ => "VHD", _ => source, _ => verbose)
                .WithCheckedVhdType(_ => source, _ => VirtualHardDiskType.Fixed)
                .Map(action);
        }
    }
}
