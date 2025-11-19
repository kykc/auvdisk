using System.Security.Cryptography;
using DiscUtils.Streams;
using DiskAccessLibrary.VHD;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace auvdisk.test;

public static class TestUtil
{
    public static VHDFooter? ReadVhdFooter(string path)
    {
        if (File.Exists(path))
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);

            if (stream.Length > 512)
            {
                stream.Seek(-512, SeekOrigin.End);
                var footerBytes = new byte[512];
                stream.ReadExactly(footerBytes);
                
                return new VHDFooter(footerBytes);
            }
        }

        return null;
    }
    
    public static string CalcSha256Hash(string file)
    {
        using FileStream stream = File.OpenRead(file);

        return CalcSha256Hash(stream);
    }

    public static string CalcSha256Hash(Stream stream)
    {
        var sha = SHA256.Create();
        byte[] checksum = sha.ComputeHash(stream);
        return BitConverter.ToString(checksum).Replace("-", String.Empty);
    }
    
    public static uint LazyFastDiskHash(DiscUtils.VirtualDisk disk)
    {
        var streams = disk.Content.Extents.Select(x => new SubStream(disk.Content, Ownership.None, x.Start, x.Length));
        return CalculateAdler32(streams);
    }
    
    internal static uint CalculateAdler32(IEnumerable<Stream> streams)
    {
        const uint modAdler = 65521;
        
        uint a = 1;
        uint b = 0;
        
        const int bufferSize = 1024 * 1024; // 1MiB 
        var buffer = new byte[bufferSize];

        foreach (var stream in streams)
        {
            int bytesRead;
            stream.Seek(0, SeekOrigin.Begin);

            while ((bytesRead = stream.Read(buffer, 0, bufferSize)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    // A is the sum of the data bytes (modulo MOD_ADLER)
                    a = (a + buffer[i]) % modAdler;

                    // B is the sum of the previous values of A (modulo MOD_ADLER)
                    b = (b + a) % modAdler;
                }
            }
        }

        // The final checksum is (B * 65536) + A
        return (b << 16) | a;
    }
}
