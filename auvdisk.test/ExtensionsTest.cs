using auvdisk.Extensions;

namespace auvdisk.test
{
    public class ExtensionsTest
    {
        [Fact]
        public void TestParseLengthInBytes()
        {
            Assert.Null("".ParseByteLength());
            Assert.Null("dummy".ParseByteLength());
            Assert.Null("123dummy".ParseByteLength());
            Assert.Null("123PiB".ParseByteLength());
            Assert.Equal(123UL, "123".ParseByteLength());
            Assert.Equal(123UL, "123B".ParseByteLength());
            Assert.Equal(124UL, "123.6".ParseByteLength());
            Assert.Equal(124UL, "123.4".ParseByteLength());
            Assert.Equal(5242880UL, "5MiB".ParseByteLength());
            Assert.Equal(5242880UL, "5mib".ParseByteLength());
            Assert.Equal(1024UL, "1KiB".ParseByteLength());
            Assert.Equal(2684354560UL, "2.5GiB".ParseByteLength());
            Assert.Equal(2748779069440UL, "2.5TiB".ParseByteLength());
            Assert.Equal(2748779069440UL, "2.5 TiB".ParseByteLength());
            Assert.Equal(1024UL, "1,024".ParseByteLength());
        }
    }
}