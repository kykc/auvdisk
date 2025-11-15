using auvdisk.Extensions;
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
    
    public static void InitializeDisk(DiscUtils.VirtualDisk disk, long firstUsableLba, List<GuidPartitionEntry> partitionEntries)
    {
        GuidPartitionTable.InitializeDisk(new DiskAdapter(disk), firstUsableLba, partitionEntries);
    }
}