using auvdisk.Extensions;
using DiscUtils;
using DiskAccessLibrary;

namespace auvdisk.DiskImage;

public static class Util
{
    /// <summary>
    /// Create image file and initialize it with GPT partition table with two partitions:
    /// 1. EFI boot
    /// 2. "data" partition
    /// </summary>
    /// <param name="target"></param>
    /// <param name="bootSizeInBytes"></param>
    /// <param name="dataSizeInBytes"></param>
    /// <param name="logger"></param>
    /// <param name="zeroFill"></param>
    /// <param name="dynamic"></param>
    /// <param name="vhdx">Create VHDx instead of VHD</param>
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

        return CreateVdisk(target, totalSize, logger, dynamic, zeroFill, vhdx)
            .Concat(_ => PartitionTable.Util.CreateLayout(bootSizeInBytes > 0 ? [bootSizeInBytes, 0] : [0], totalSize, Vhd.Util.LbaSize, logger, offsetLba, bootSizeInBytes == 0))
            .Bind(tuple =>
            {
                var disk = tuple.Item1;
                var list = tuple.Item2;

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
            .MapDispose(_ => new Value<ulong>(dataSizeInBytes));
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

            return result.Map(vhdInfo => VirtualDisk.OpenDisk(vhdInfo.Path, "vhd", FileAccess.ReadWrite, "", ""));
        }
        else
        {
            var result = dynamic
                ? Vhdx.Util.CreateDynamic(target, totalSize, logger)
                : Vhdx.Util.CreateFixed(target, totalSize, logger, zeroFill);

            return result.Map(x => x as VirtualDisk);
        }
    }

    public static bool IsSuccess(this DiskProbe.ProbeResult result)
    {
        return result.Disk.IsSome() || result.Fs.IsSome();
    }
}