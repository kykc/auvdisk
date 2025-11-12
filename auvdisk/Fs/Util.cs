#if WINDOWS
using auvdisk.Interop;
#endif
using auvdisk.Bytes;
using auvdisk.Extensions;

namespace auvdisk.Fs
{
    public static class Util
    {
#pragma warning disable CA1416
        public static bool ResizeFileFastUnsafe(string target, ulong size, Log.ILog logger)
        {
#if WINDOWS
            return auvdisk.Interop.Win32.Util.ResizeFileFastUnsafe(target, size, logger).IsSuccess;
#else
            logger.Error("This operation is not supported on this platform");
            return false;
#endif
        }

        public static bool? IsSparseFile(string target, Log.ILog logger)
        {
#if WINDOWS
            return auvdisk.Interop.Win32.Util.IsSparseFile(target, logger);
#else
            logger.Error("This operation is not supported on this platform");
            return null;
#endif
        }

#pragma warning restore CA1416

        // TODO: implement progress for zero fill?
        public static void ResizeFile(string target, ulong size, Log.ILog logger)
        {
            var stream = new FileStream(target, FileMode.OpenOrCreate);
            stream.Seek((long)size - 1, SeekOrigin.Begin);
            stream.WriteByte(0);
            stream.Close();
        }

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

        public static string? GetUuid(this DiscUtils.DiscFileSystem fs, Log.ILog logger)
        {
            return ExtractUuid(fs, logger);
        }
        
        public static void ExtractFileSegment(string source, string target, ulong offset, ulong length)
        {
            using var rawFileStream = new System.IO.FileStream(source, FileMode.Open, FileAccess.Read);
            using var decoratedStream = new SegmentStream(rawFileStream, (long)offset, (long)length);
            using var targetStream = new System.IO.FileStream(target, FileMode.Create, FileAccess.Write);
            decoratedStream.CopyTo(targetStream);
        }
    }
}
