using auvdisk.DiskImage.Vhd;
using auvdisk.Extensions;
using auvdisk.Log;
using DiskAccessLibrary;

namespace auvdisk.PartitionTable;

public static class Util
{
    internal class DiskAdapter(DiscUtils.VirtualDisk disk) : Disk
    {
        public override byte[] ReadSectors(long sectorIndex, int sectorCount)
        {
            var buffer = new byte[sectorCount * BytesPerSector];
            disk.Content.Position = sectorIndex * BytesPerSector;
            disk.Content.ReadExactly(buffer);

            return buffer;
        }

        public override void WriteSectors(long sectorIndex, byte[] data)
        {
            disk.Content.Position = sectorIndex * BytesPerSector;
            disk.Content.Write(data, 0, data.Length);
        }

        public override int BytesPerSector => disk.SectorSize;
        public override long Size => disk.Capacity;
    }

    public record GuidPartitionCreateInfo(ulong LengthInBytes, Guid Type);
    public record GuidPartitionTableCreateInfo(ulong DiskLengthInBytes, ulong LbaSize, ulong OffsetLba, List<GuidPartitionCreateInfo> Partitions);

    public static Flow<ulong[]> ParseLayout(string subj, ILog logger)
    {
        var tokens = subj.Split(",").Select(x => x.Trim().ParseByteLength()).ToList();

        if (tokens.Any(x => !x.HasValue))
        {
            return new($"Failed to parse layout <{subj}>", logger);
        }

        return Flows.Ok(tokens.Select(x => x!.Value).ToArray(), logger);
    }
    
    public static void InitializeDisk(DiscUtils.VirtualDisk disk, long firstUsableLba, List<GuidPartitionEntry> partitionEntries)
    {
        GuidPartitionTable.InitializeDisk(new DiskAdapter(disk), firstUsableLba, partitionEntries);
    }

    public static Flow<string> InitializeDisk(string targetPath, ulong size, string partitions, bool markBoot, ILog logger)
    {
        return ParseLayout(partitions, logger)
            .Bind(partSizes => CreateLayout(partSizes, size, DiskImage.Vhd.Util.LbaSize, logger, 2048UL, !markBoot))
            .Bind(layout =>
            {
                var ptString = string.Join(", ", layout.Select(x => $"({x.FirstLBA}-{x.LastLBA})"));
                var lbaSize = auvdisk.DiskImage.Vhd.Util.LbaSize;
                
                try
                {
                    logger.Log($"Initializing {targetPath} with GPT partition table (boundaries in LBA, LBA Size = {lbaSize}): {ptString}");
                    using var duDisk = DiscUtils.VirtualDisk.OpenDisk(targetPath, FileAccess.ReadWrite);
                    InitializeDisk(duDisk, 2048L, layout);

                    return Flows.Ok(targetPath, logger);
                }
                catch (Exception ex)
                {
                    return new(ex.Message, logger);
                }
            });
    }
    
    public static Flow<VhdFileInfo> InitializeDisk(VhdFileInfo vhdInfo, ulong size, string partitions, bool markBoot, ILog logger)
    {
        return InitializeDisk(vhdInfo.Path, size, partitions, markBoot, logger).Map(_ => vhdInfo);
    }

    public static Flow<List<GuidPartitionEntry>> CreateLayout(ulong[] sizes, ulong diskSize, ulong lbaSize, ILog logger,
        ulong offsetLba = 2048, bool noBoot = false)
    {
        var parts = sizes.Select((x, idx) =>
            new GuidPartitionCreateInfo(x, (idx == 0 && !noBoot) ? GPTPartition.EFISystemPartitionTypeGuid : GPTPartition.BasicDataPartititionTypeGuid));

        return CreateLayout(new GuidPartitionTableCreateInfo(diskSize, lbaSize, offsetLba, parts.ToList()), logger);
    }
    
    public static Flow<List<GuidPartitionEntry>> CreateLayout(GuidPartitionTableCreateInfo task, ILog logger)
    {
        var partsInfo = task.Partitions;
        var overhead = 1024UL * 1024; // 1 MiB;

        if (partsInfo.Count == 0)
        {
            return new("Empty partition list", logger);
        }
        
        if (partsInfo.Take(partsInfo.Count - 1).Any(x => x.LengthInBytes == 0))
        {
            return new("Partition size cannot be zero", logger);
        }

        var totalAllocated = partsInfo.Select(x => x.LengthInBytes).Aggregate((x, y) => x + y) + overhead + task.OffsetLba * task.LbaSize;

        if (totalAllocated > task.DiskLengthInBytes)
        {
            return new("Total length of requested partitions exceeds disk size", logger);
        }

        if (partsInfo.Any(x => x.LengthInBytes % task.LbaSize > 0))
        {
            return new($"Partition size must be a multiple of LbaSize {task.LbaSize}", logger);
        }
        
        var result = new List<GuidPartitionEntry>();
        var currentOffset = task.OffsetLba;

        foreach (var partition in partsInfo.Take(partsInfo.Count - 1))
        {
            var entry = new GuidPartitionEntry();
            entry.PartitionGuid = Guid.NewGuid();
            entry.PartitionTypeGuid = partition.Type;
            entry.FirstLBA = currentOffset;
            entry.LastLBA = currentOffset + partition.LengthInBytes / task.LbaSize - 1;

            currentOffset = entry.LastLBA + 1;
            
            result.Add(entry);
        }
        
        var lastPartition = partsInfo.Last();
        var lastPartitionLastLba = currentOffset + lastPartition.LengthInBytes / task.LbaSize - 1;
        
        // Extend until the end of the disk
        if (lastPartition.LengthInBytes == 0)
        {
            // This one is a bit magical to me, snatched from DiscAccessLibrary internals. Probably accounts for GPT partition table footer
            //long partitionEntriesSecondaryLBA = disk.TotalSectors - 1 - partitionEntriesLength / disk.BytesPerSector;
            lastPartitionLastLba = (task.DiskLengthInBytes / task.LbaSize - 1 - 16384 / task.LbaSize) - 1;
        }
        
        result.Add(new GuidPartitionEntry
        {
            PartitionGuid = Guid.NewGuid(),
            PartitionTypeGuid = lastPartition.Type,
            FirstLBA = currentOffset,
            LastLBA = lastPartitionLastLba
        });

        return Flows.Ok(result, logger);
    }
}