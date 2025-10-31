using DiscUtils;
using DiscUtils.HfsPlus;

namespace auvdisk.test
{
    [Collection("Sequential")]
    public class DiscUtilsTest
    {
        [Fact]
        public void TestDmgHfsPlus()
        {
            // This test file contains circular symlink pointing to self.
            // This was leading to stack overflow in DU
            // I guess I'll leave the test be to test against regressions
            string target = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dmg_test.dmg");

            using var disk = VirtualDisk.OpenDisk(target, FileAccess.Read);

            var fs = new HfsPlusFileSystem(disk.Partitions.Partitions[4].Open());

            var files = fs.GetFiles("").ToList();

            Assert.Contains("LICENSE", files.Select(f => f.TrimStart('\\', '/')));
        }
    }
}