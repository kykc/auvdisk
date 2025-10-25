using DiscUtils;
using System;

namespace auvdisk.Fs.Fat
{
    public static class UuidExtractor
    {
        public static string? ExtractUuid(DiscFileSystem fs, Log.ILog logger)
        {
            var id = fs.VolumeId;

            var bytes = BitConverter.GetBytes(id);

            if (BitConverter.IsLittleEndian)
            {
                bytes = bytes.Reverse().ToArray();
            }

            var firstPart = bytes.Take(2).ToArray();
            var secondPart = bytes.Skip(2).ToArray();
            
            return $"{System.Convert.ToHexString(firstPart)}-{System.Convert.ToHexString(secondPart)}";
        }
    }    
}