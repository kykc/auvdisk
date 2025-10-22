using System.Text;
using DiskAccessLibrary.VHD;

namespace auvdisk.DiskImage.Vhd
{
    public class DynamicDiskHeader : DiskAccessLibrary.VHD.DynamicDiskHeader
    {
        const int NameOffset = 64;
        const int NameLength = 512;
        
        /* From VHD specification:
            None (0x0)	
            Wi2r (0x57693272)	[deprecated]
            Wi2k (0x5769326B)	[deprecated]
            W2ru (0x57327275)	Unicode pathname (UTF-16) on Windows relative to the differencing disk pathname.
            W2ku (0x57326B75)	Absolute Unicode (UTF-16) pathname on Windows.
            Mac (0x4D616320)	(Mac OS alias stored as a blob)
            MacX(0x4D616358)	A file URL with UTF-8 encoding conforming to RFC 2396.
        */
        public enum ParentLocatorPlatformCode
        {
            None = 0x0,
            Deprecated1 = 0x57693272,
            Deprecated2 = 0x5769326B,
            WindowsUtf16Relative = 0x57327275,
            WindowsUtf16Absolute = 0x57326B75,
            MacAliasBlob = 0x4D616320,
            MacXUtf8FileUrlRfc2396 = 0x4D616358
        }

        public DynamicDiskHeader() : base()
        {
        }
        
        public DynamicDiskHeader(byte[] headerBytes) : base(headerBytes)
        {
            // Taken from VHD specification
            
            // Bug in DiskAccessLibrary. Author forgot that UTF16 is also in big endian and bytes should be swapped
            var nameBytes = headerBytes.Skip(NameOffset).Take(NameLength).ToArray();
            ParentUnicodeName = Encoding.BigEndianUnicode.GetString(nameBytes).TrimEnd('\0');
        }

        public new byte[] GetBytes()
        {
            var bytes = base.GetBytes();

            // Bug in DiskAccessLibrary. Author forgot that UTF16 is also in big endian and bytes should be swapped
            byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(ParentUnicodeName).Take(NameLength).ToArray();
            Array.Copy(nameBytes, 0, bytes, NameOffset, nameBytes.Length);

            // Recalculate checksum. We need it because upstream is plagued with bug which
            // sums only first 512 bytes. Also, I'm not sure about the ParentUnicodeName.
            // We're only swapping bytes, so it shouldn't affect the sum. IDK
            Bytes.Util.WriteBytes(bytes, new byte[4], 0x24);

            var checksum = Util.CalculateChecksum(bytes, 0x24);
            
            Bytes.Util.WriteBytes(bytes, Bytes.Util.ToBigEndian(checksum), 0x24);
            
            return bytes;
        }

        public IEnumerable<ParentLocatorEntry> GetParentLocatorEntries()
        {
            List<ParentLocatorEntry> list =
            [
                ParentLocatorEntry1,
                ParentLocatorEntry2,
                ParentLocatorEntry3,
                ParentLocatorEntry4,
                ParentLocatorEntry5,
                ParentLocatorEntry6,
                ParentLocatorEntry7,
                ParentLocatorEntry8
            ];

            return list.Where(l => l.PlatformCode != 0);
        }

        public static string? ReadParentLocator(FileStream stream, ParentLocatorEntry locator)
        {
            var platformCode = (ParentLocatorPlatformCode)locator.PlatformCode;

            if (platformCode != ParentLocatorPlatformCode.None)
            {
                var bytes = new byte[locator.PlatformDataLength];

                stream.Seek((int)locator.PlatformDataOffset, SeekOrigin.Begin);
                stream.ReadExactly(bytes);
                
                // For those VHD reference doesn't specify endianness. Testing it
                // creating VHD by Windows itself those end up being little endian for some reason
                if (platformCode == ParentLocatorPlatformCode.WindowsUtf16Absolute)
                {
                    return Encoding.Unicode.GetString(bytes);
                }
                else if (platformCode == ParentLocatorPlatformCode.WindowsUtf16Relative)
                {
                    return Encoding.Unicode.GetString(bytes);
                }
                else if (platformCode == ParentLocatorPlatformCode.MacXUtf8FileUrlRfc2396) // WARNING: never tested this
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }

            return null;
        }
    }
}