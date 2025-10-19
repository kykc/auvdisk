using DiscUtils;
using System;

namespace auvdisk.Fat
{
    public static class Util
    {
        public static string? ExtractUuid(DiscFileSystem fs, Action<string> logger)
        {
            var id = fs.VolumeId;
            // TODO: proper endianness
            var bytes = BitConverter.GetBytes(id).Reverse().ToArray();

            var firstPart = bytes.Take(2).ToArray();
            var secondPart = bytes.Skip(2).ToArray();
            
            return $"{System.Convert.ToHexString(firstPart)}-{System.Convert.ToHexString(secondPart)}";
        }
    }    
}