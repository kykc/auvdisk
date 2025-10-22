#if WINDOWS
using auvdisk.Interop;
#endif
using auvdisk.Bytes;

namespace auvdisk.Fs
{
    public class Util
    {
#pragma warning disable CA1416
        public static void CreateFileFastUnsafe(string target, ulong size)
        {
#if WINDOWS
            Win32.CreateFileFastUnsafe(target, size);
#else
            var stream = new FileStream(target, FileMode.CreateNew);
            stream.Seek((long)size - 1, SeekOrigin.Begin);
            stream.WriteByte(0);
            stream.Close();
#endif
        }
#pragma warning restore CA1416

        public static string? ExtractUuid(DiscUtils.DiscFileSystem fs, Log.ILog logger)
        {
            if (fs is DiscUtils.Ntfs.NtfsFileSystem)
            {
                return Ntfs.UuidExtractor.ExtractUuid(fs, logger);
            }
            else if (fs is DiscUtils.Fat.FatFileSystem)
            {
                return Fat.UuidExtractor.ExtractUuid(fs, logger);
            }
            else if (fs is DiscUtils.Ext.ExtFileSystem)
            {
                return Ext.UuidExtractor.ExtractUuid(fs, logger).ToString();
            }
            else
            {
                return null;
            }
        }
        
        public static void ExtractFileSegment(string source, string target, ulong offset, ulong length)
        {
            using var rawFileStream = new FileStream(source, FileMode.Open, FileAccess.Read);
            using var decoratedStream = new SegmentStream(rawFileStream, (long)offset, (long)length);
            using var targetStream = new FileStream(target, FileMode.Create, FileAccess.Write);
            decoratedStream.CopyTo(targetStream);
        }
    }
}
