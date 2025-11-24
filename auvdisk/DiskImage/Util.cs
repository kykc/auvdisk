using auvdisk.Bytes;
using auvdisk.DiskImage.Vhd;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using DiscUtils.Streams;
using DiskAccessLibrary;
using DiskAccessLibrary.VHD;

namespace auvdisk.DiskImage;

public static class Util
{
    /// <summary>
    /// Create image file and initialize it with GPT partition table with two partitions:
    /// 1. EFI boot
    /// 2. "data" partition
    /// </summary>
    /// <returns>size in bytes of the data partition</returns>
    public static Flow<Value<ulong>> CreateVdiskWithGptLayout(string target, ulong bootSizeInBytes, ulong dataSizeInBytes, Log.ILog logger, bool zeroFill = false, bool dynamic = false, bool vhdx = false)
    {
        var typeString = vhdx ? "VHDx" : "VHD";
        logger.Log($"Creating {(dynamic ? "dynamic" : "fixed")} {typeString} layout");
        dataSizeInBytes = Vhd.Util.RoundUp(dataSizeInBytes, vhdx ? 1024 * 1024 : Vhd.Util.LbaSize);
        logger.Log($"Rounded up data partition size is {dataSizeInBytes}");

        const ulong offsetLba = 2048UL; // Start first partition from the sector/LBA 2048, this is what Windows does AFAIK
        const ulong overheadSize = 1024UL * 1024UL; // 1MiB for partition table and stuff

        ulong totalSize = offsetLba * Vhd.Util.LbaSize + overheadSize + bootSizeInBytes + dataSizeInBytes;
        logger.Log($"Total size of the image contents is {totalSize}");

        // Pinning this to scope, as it has IDisposables
        using var result = CreateVdisk(target, totalSize, logger, dynamic, zeroFill, vhdx)
            .BindConcat(
                _ => PartitionTable.Util.CreateLayout(bootSizeInBytes > 0 ? [bootSizeInBytes, 0] : [0], totalSize, Vhd.Util.LbaSize, logger, offsetLba, bootSizeInBytes == 0),
                (disk, list) => new {disk, list})
            .Bind(tuple =>
            {
                var disk = tuple.disk;
                var list = tuple.list;

                if (list.Count == 2)
                {
                    var bootPartitionEntry = list.First();
                    var dataPartitionEntry = list.Skip(1).First();

                    logger.Log($"Initializing {typeString} with GPT partition table");
                    logger.Log($"Boot partition space in LBA is from {bootPartitionEntry.FirstLBA} to {bootPartitionEntry.LastLBA}");
                    logger.Log($"Data partition space in LBA is from {dataPartitionEntry.FirstLBA} to {dataPartitionEntry.LastLBA}");
                }
                else
                {
                    var dataPartitionEntry = list.First();
                    
                    logger.Log($"Initializing {typeString} with GPT partition table");
                    logger.Log($"Data partition space in LBA is from {dataPartitionEntry.FirstLBA} to {dataPartitionEntry.LastLBA}");
                }

                PartitionTable.Util.InitializeDisk(disk, (long)offsetLba, list);

                return Flows.Val(disk);
            })
            .MapDispose(_ => dataSizeInBytes.RefVal());

        return result.IsErr ? new(result.UnwrapErr()) : Flows.Val(result.UnwrapVal());
    }
    
    /// <summary>
    /// CAUTION: underlying VirtualDisk should be properly disposed when appropriate as it holds the handle to the actual file.
    /// </summary>
    public static Flow<VirtualDisk> CreateVdisk(string target, ulong totalSize, Log.ILog logger, bool dynamic = false, bool zeroFill = false, bool vhdx = false)
    {
        if (!vhdx)
        {
            var result = dynamic
                ? Vhd.Util.CreateDynamicVhd(target, totalSize, logger)
                : Vhd.Util.CreateFixedVhd(target, totalSize, logger, zeroFill);

            return result.MapOr(vhdInfo => VirtualDisk.OpenDisk(vhdInfo.Path, "vhd", FileAccess.ReadWrite, "", ""), "Failed to open VHD");
        }
        else
        {
            var result = dynamic
                ? Vhdx.Util.CreateDynamic(target, totalSize, logger)
                : Vhdx.Util.CreateFixed(target, totalSize, logger, zeroFill);

            return result.Map(VirtualDisk (x) => x);
        }
    }

    public static Flow<VirtualDisk> CreateDiffVdisk(string path, string parentPath, ILog logger, bool vhdx = false)
    {
        if (!vhdx)
        {
            var result = Vhd.Util.CreateDifferentialVhd(parentPath, path, logger);

            return result.Bind(vhdInfo => Vhd.Util.OpenDiskWithDu(vhdInfo.Path, logger)).Map(VirtualDisk (x) => x);
        }
        else
        {
            var result = Vhdx.Util.CreateDifferencing(path, parentPath, logger);

            return result.Map(VirtualDisk (x) => x);
        }
    }

    // Using extents copies only allocated data in case of dynamic VHD or other
    // underlying type supporting dynamic allocation
    // CAUTION: this function might not do what you want/expect if the passed source is {IsSparse: true, NeedsParent: true},
    // (like differencing VHD, for example). In this case source.Contents will have all the date including all the parent(s).
    // There's also not a lot of sanity checks there, things are supposed to be checked on the caller's side
    public static Flow<None> LazyCopyDiskContents(VirtualDisk source, VirtualDisk destination, ILog logger)
    {
        if (source.Capacity != destination.Capacity)
        {
            return new($"Source/destination disk image capacity mismatch ({source.Capacity} and {destination.Capacity}) when trying to copy image contents");
        }
        
        logger.Log("Calculating amount of data to be copied...");
        var toCopyLength = source.Content.Extents.Select(x => x.Length).Sum();
        var progressData = new StreamCopyProgressWrapper.ProgressData("Copying", toCopyLength);
        Utils.WithProgress(logger, progressData, progress =>
        {
            foreach (var extent in source.Content.Extents)
            {
                var sourceSubstream = new SubStream(source.Content, extent.Start, extent.Length);
                destination.Content.Seek(extent.Start, SeekOrigin.Begin);
                StreamCopyProgressWrapper.CopyTo(sourceSubstream, destination.Content, logger, progressData, progress);
            }

            return progressData;
        });
                
        destination.Content.Flush();

        return Flows.Val(None.Value);
    }

    public static Flow<Value<VirtualHardDiskType>> DetectDiskType(VirtualDisk source)
    {
        if (source.DiskTypeInfo.Name != "VHD")
        {
            return new($"Unexpected disk type <{source.DiskTypeInfo.Name}>");
        }
        else
        {
            var targetLayer = source.Layers.First();

            return targetLayer.IsSparse switch
            {
                false when !targetLayer.NeedsParent => VirtualHardDiskType.Fixed.RefVal().Flow(),
                true when targetLayer.NeedsParent => VirtualHardDiskType.Differencing.RefVal().Flow(),
                true when !targetLayer.NeedsParent => VirtualHardDiskType.Dynamic.RefVal().Flow(),
                _ => new("Unexpected disk type")
            };
        }
    }

    public static bool IsSuccess(this DiskProbe.ProbeResult result)
    {
        return result.Disk.IsSome() || result.Fs.IsSome();
    }
}