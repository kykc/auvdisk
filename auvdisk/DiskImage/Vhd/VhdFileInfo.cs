using DiskAccessLibrary.VHD;

namespace auvdisk.DiskImage.Vhd
{
    public class VhdFileInfo
    {
        public ulong CapacityInBytes { get; init; } = 0;
        public ulong BytesPerSector { get; init; } = 0;
        public string Path { get; init; } = "";
        public ulong TotalSectors => CapacityInBytes / BytesPerSector;
        public VirtualHardDiskType DiskType { get; init; }

        private VhdFileInfo()
        {
        }
        
        public VhdFileInfo(DiskAccessLibrary.VirtualHardDisk disk)
        {
            CapacityInBytes = (ulong)disk.Size;
            BytesPerSector = (ulong)disk.BytesPerSector;
            Path = disk.Path;
            DiskType = disk.Footer.DiskType;
        }

        public VhdFileInfo(DiscUtils.Vhd.Disk disk, string path, VirtualHardDiskType diskType, bool dispose = false)
        {
            CapacityInBytes = (ulong)disk.Capacity;
            BytesPerSector = (ulong)disk.BlockSize;
            Path = path;
            DiskType = diskType;

            if (dispose)
            {
                disk.Dispose();
            }
        }

        public VhdFileInfo(VHDFooter footer, string path, ulong bytesPerSector)
        {
            CapacityInBytes = footer.CurrentSize;
            BytesPerSector = bytesPerSector;
            Path = path;
            DiskType = footer.DiskType;
        }

        public static VhdFileInfo? Make(DiscUtils.VirtualDisk disk, string path, VirtualHardDiskType diskType, bool dispose = false)
        {
            if (disk.DiskTypeInfo.Name.Equals("vhd", StringComparison.InvariantCultureIgnoreCase))
            {
                var result = new VhdFileInfo
                {
                    DiskType = diskType,
                    Path = path,
                    BytesPerSector = (ulong)disk.BlockSize,
                    CapacityInBytes = (ulong)disk.Capacity
                };
                
                if (dispose) disk.Dispose();
                
                return result;
            }

            return null;
        }
    }
}