using DiscUtils;

namespace auvdisk.Fs.Ntfs
{
    public static class UuidExtractor
    {
        // Constants for the NTFS Partition Boot Sector (PBS)
        // The PBS starts at offset 0 of the partition/image.
        private const int NtfsPbsOffset = 0;
    
        // The NTFS Volume Serial Number (UUID) is located at offset 0x48 (72 bytes) 
        // within the Partition Boot Sector.
        private const int NtfsVolumeIdOffsetInPbs = 0x48; 
        private const int VolumeIdLength = 8; // NTFS Volume ID is an 8-byte value (two 4-byte integers)
    
        // The total absolute offset in the image file (0 + 72)
        private const int AbsoluteVolumeIdOffset = NtfsPbsOffset + NtfsVolumeIdOffsetInPbs;

        
        public static string? ExtractUuid(DiscFileSystem fs, Log.ILog logger)
        {
            var fsStream = fs.RawStream;
            
            if (fsStream.Length < AbsoluteVolumeIdOffset + VolumeIdLength)
            {
                logger.Error("Image file is too small to contain the NTFS Volume Serial Number.");
                return null;
            }

            try
            {
                fsStream.Seek(AbsoluteVolumeIdOffset, SeekOrigin.Begin);

                byte[] volumeIdBytes = new byte[VolumeIdLength];
                
                fsStream.ReadExactly(volumeIdBytes);
                
                // Convert the 8 bytes into the standard NTFS Volume Serial Number format (FFFF-FFFF-FFFF-FFFF)
                // We use BitConverter to interpret the two 32-bit integers that make up the 8-byte volume ID.
                var part1 = BitConverter.ToUInt32(volumeIdBytes, 0);
                var part2 = BitConverter.ToUInt32(volumeIdBytes, 4);
                
                // Format as a standard 8-hex-digit-per-part string (e.g., 1A2B3C4D5E6F7081)
                // TODO: proper endianness
                return $"{part2:X8}{part1:X8}";
            }
            catch (IOException ex)
            {
                logger.Error($"{ex.Message}");
                return null;
            }
        }
    }    
}
