using DiscUtils;

namespace auvdisk.Ext
{
    public static class Util
    {
        private const int Ext4SuperblockOffset = 1024;
        private const int Ext4UuidOffsetInSuperblock = 104;
        private const int UuidLength = 16;
        private const int AbsoluteUuidOffset = Ext4SuperblockOffset + Ext4UuidOffsetInSuperblock;
        private static readonly byte[] Ext4MagicValue = [0xEF, 0x53];
        private const int Ext4MagicOffsetInSuperblock = 56;
        private const int AbsoluteMagicOffset = Ext4SuperblockOffset + Ext4MagicOffsetInSuperblock;

        public static Guid? ExtractUuid(DiscFileSystem fs, Action<string> logger)
        {
            var fsStream = fs.RawStream;
            
            if (fsStream.Length < AbsoluteUuidOffset + UuidLength)
            {
                logger("ERROR: Image file is too small to contain the ext4 superblock UUID.");
                return null;
            }

            try
            {
                fsStream.Seek(AbsoluteMagicOffset, SeekOrigin.Begin);
                var magicBytes = new byte[sizeof(UInt16)];
                fsStream.ReadExactly(magicBytes);
                
                // TODO: proper endinnaness check
                if (!magicBytes.SequenceEqual(Ext4MagicValue) && !magicBytes.Reverse().SequenceEqual(Ext4MagicValue))
                {
                    logger($"ERROR: ext4 magic value not found");
                    return null;
                }
                
                fsStream.Seek(AbsoluteUuidOffset, SeekOrigin.Begin);

                var uuidBytes = new byte[UuidLength];
                
                fsStream.ReadExactly(uuidBytes);

                // TODO: proper endianness check
                var uuid = new Guid(uuidBytes, true);

                return uuid;
            }
            catch (IOException ex)
            {
                logger($"ERROR: {ex.Message}");
                return null;
            }
        }
    }    
}