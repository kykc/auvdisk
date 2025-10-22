using DiskAccessLibrary;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class VhdUtilTest
    {
        [Fact]
        public void TestRelativePath()
        {
            // Windows
            Assert.Equal(@".\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\child.vhd", @"D:\parent.vhd"));
            Assert.Equal(@".\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\test\child.vhd", @"D:\test\parent.vhd"));
            Assert.Equal(@"..\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\test\child.vhd", @"D:\parent.vhd"));
            Assert.Equal(@".\test\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\child.vhd", @"D:\test\parent.vhd"));
            Assert.Equal(@"L:\test_parent\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\test\child.vhd", @"L:\test_parent\parent.vhd"));
            Assert.Throws<InvalidPathException>(() => DiskImage.Vhd.Util.NormalizeRelativePathToParent(@".\test\child.vhd", @"D:\parent.vhd"));
            Assert.Throws<InvalidPathException>(() => DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"D:\test\child.vhd", @".\parent.vhd"));
            
            // Posix
            Assert.Equal(@".\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent("/mnt/d/child.vhd", "/mnt/d/parent.vhd"));
            Assert.Equal(@"..\parent.vhd",  DiskImage.Vhd.Util.NormalizeRelativePathToParent("/mnt/d/test/child.vhd", "/mnt/d/parent.vhd"));
            Assert.Equal(@".\test\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent("/mnt/d/child.vhd", "/mnt/d/test/parent.vhd"));
            Assert.Equal(@"..\test2\parent.vhd", DiskImage.Vhd.Util.NormalizeRelativePathToParent("/mnt/d/test/child.vhd", "/mnt/d/test2/parent.vhd"));
            Assert.Throws<InvalidPathException>(() => DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"./test/child.vhd", @"/mnt/d/parent.vhd"));
            Assert.Throws<InvalidPathException>(() => DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"/mnt/d/test/child.vhd", @"./parent.vhd"));
            
            // Mixed
            Assert.Throws<InvalidPathException>(() => DiskImage.Vhd.Util.NormalizeRelativePathToParent(@"C:\test\child.vhd", "/mnt/d/parent.vhd"));
        }
    }
}