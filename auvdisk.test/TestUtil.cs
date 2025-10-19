using System.Security.Cryptography;
using DiskAccessLibrary.VHD;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace auvdisk.test;

public class TestUtil
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
        using (FileStream stream = File.OpenRead(file))
        {
            var sha = SHA256.Create();
            byte[] checksum = sha.ComputeHash(stream);
            return BitConverter.ToString(checksum).Replace("-", String.Empty);
        }
    }
}
