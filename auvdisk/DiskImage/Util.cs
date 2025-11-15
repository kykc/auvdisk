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
    public static Flow<Value<ulong>> CreateBootableLayout(string target, ulong bootSizeInBytes, ulong dataSizeInBytes, Log.ILog logger, bool zeroFill = false, bool dynamic = false, bool vhdx = false)
    {
        var typeString = vhdx ? "VHDx" : "VHD";
        logger.Log($"Creating {(dynamic ? "dynamic" : "fixed")} {typeString} layout");
        dataSizeInBytes = Vhd.Util.RoundUp(dataSizeInBytes, vhdx ? 1024 * 1024 : Vhd.Util.LbaSize);
        logger.Log("Rounded up data partition size is " + dataSizeInBytes.ToString());

        const ulong offsetLba = 2048UL; // Start first partition from the sector/LBA 2048, this is what Windows does AFAIK
        const ulong overheadSize = 1024UL * 1024UL; // 1MiB for partition table and stuff

        ulong totalSize = offsetLba * Vhd.Util.LbaSize + overheadSize + bootSizeInBytes + dataSizeInBytes;
        logger.Log($"Total size of the image contents is {totalSize}");

        return MakeDisk()
            .Map(disk =>
            {
                List<GuidPartitionEntry> list = new List<GuidPartitionEntry>();

                GuidPartitionEntry bootPartitionEntry = new GuidPartitionEntry();
                bootPartitionEntry.PartitionGuid = Guid.NewGuid();
                bootPartitionEntry.PartitionTypeGuid = GPTPartition.EFISystemPartitionTypeGuid;
                bootPartitionEntry.FirstLBA = offsetLba;
                bootPartitionEntry.LastLBA = offsetLba + bootSizeInBytes / Vhd.Util.LbaSize - 1;
                bootPartitionEntry.PartitionName = "Boot";
                list.Add(bootPartitionEntry);

                GuidPartitionEntry dataPartitionEntry = new GuidPartitionEntry();
                dataPartitionEntry.PartitionGuid = Guid.NewGuid();
                dataPartitionEntry.PartitionTypeGuid = GPTPartition.BasicDataPartititionTypeGuid;
                dataPartitionEntry.FirstLBA = bootPartitionEntry.LastLBA + 1;
                // This one is a bit magical to me, snatched from DiscAccessLibrary internals. Probably accounts for GPT partition table footer
                dataPartitionEntry.LastLBA = (ulong)(disk.Capacity / disk.SectorSize - 1 - 16384 / disk.SectorSize) - 1;
                list.Add(dataPartitionEntry);

                logger.Log($"Initializing {typeString} with GPT partition table");
                logger.Log("Boot partition space in LBA is from " + bootPartitionEntry.FirstLBA.ToString() + " to " + bootPartitionEntry.LastLBA.ToString());
                logger.Log("Data partition space in LBA is from " + dataPartitionEntry.FirstLBA.ToString() + " to " + dataPartitionEntry.LastLBA.ToString());
                
                PartitionTable.Util.InitializeDisk(disk, (long)offsetLba, list);

                return disk;
            })
            .MapDispose(_ => new Value<ulong>(dataSizeInBytes));

        Flow<VirtualDisk> MakeDisk()
        {
            if (!vhdx)
            {
                var result = dynamic
                    ? Vhd.Util.CreateDynamicVhd(target, totalSize, logger)
                    : Vhd.Util.CreateFixedVhd(target, totalSize, logger, zeroFill);

                return result.Map(vhdInfo => VirtualDisk.OpenDisk(vhdInfo.Path, FileAccess.ReadWrite));
            }
            else
            {
                var result = dynamic
                    ? Vhdx.Util.CreateDynamic(target, totalSize, logger)
                    : Vhdx.Util.CreateFixed(target, totalSize, logger, zeroFill);

                return result.Map<VirtualDisk>(x => x);
            }
        }
    }
}