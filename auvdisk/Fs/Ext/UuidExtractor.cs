using DiscUtils;

namespace auvdisk.Fs.Ext
{
    public static class UuidExtractor
    {
        private const int Ext4SuperblockOffset = 1024;
        private const int Ext4UuidOffsetInSuperblock = 104;
        private const int UuidLength = 16;
        private const int AbsoluteUuidOffset = Ext4SuperblockOffset + Ext4UuidOffsetInSuperblock;
        private static readonly byte[] Ext4MagicValue = [0x53, 0xEF];
        private const int Ext4MagicOffsetInSuperblock = 56;
        private const int AbsoluteMagicOffset = Ext4SuperblockOffset + Ext4MagicOffsetInSuperblock;

        public static Guid? ExtractUuid(Stream fsStream, Log.ILog logger)
        {
            if (fsStream.Length < AbsoluteUuidOffset + UuidLength)
            {
                logger.Error("Image file is too small to contain the ext4 superblock UUID.");
                return null;
            }

            try
            {
                fsStream.Seek(AbsoluteMagicOffset, SeekOrigin.Begin);
                var magicBytes = new byte[sizeof(UInt16)];
                fsStream.ReadExactly(magicBytes);

                if (!magicBytes.SequenceEqual(Ext4MagicValue))
                {
                    logger.Error($"ext4 magic value not found");
                    return null;
                }

                fsStream.Seek(AbsoluteUuidOffset, SeekOrigin.Begin);

                var uuidBytes = new byte[UuidLength];

                fsStream.ReadExactly(uuidBytes);

                var uuid = new Guid(uuidBytes, true);

                return uuid;
            }
            catch (IOException ex)
            {
                logger.Error($"{ex.Message}");
                return null;
            }
        }

        public static Guid? ExtractUuid(DiscFileSystem fs, Log.ILog logger)
        {
            return ExtractUuid(fs.RawStream, logger);
        }
    }    
}