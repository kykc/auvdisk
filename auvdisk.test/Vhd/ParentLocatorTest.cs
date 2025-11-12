using System.Runtime.InteropServices;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class ParentLocatorTest
    {
        [Fact]
        public void TestDiscUtilsLocator()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) // TODO: generalize to all posix?
            {
                // Relative with dot
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk("./testdata/test_gpt_child.vhd", FileAccess.Read));
                // Relative w/o dot
                CheckChild(new DiscUtils.Vhd.Disk("testdata/test_gpt_child.vhd", FileAccess.Read));
                // Absolute
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"), FileAccess.Read));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Relative with dot
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(@".\testdata\test_gpt_child.vhd", FileAccess.Read));
                // Relative w/o dot
                CheckChild(new DiscUtils.Vhd.Disk(@"testdata\test_gpt_child.vhd", FileAccess.Read));
                // Absolute
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(Path.Join(Directory.GetCurrentDirectory(), @"testdata\test_gpt_child.vhd"), FileAccess.Read));
            }
        }

        [Fact]
        public void TestDiscUtilsWithFactory()
        {
            List<string> paths =
            [
                Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"),
                Path.Join(".", "testdata", "test_gpt_child.vhd"),
                Path.Join("testdata", "test_gpt_child.vhd")
            ];

            var disks = paths.Select(p => DiscUtils.VirtualDisk.OpenDisk(p, "vhd", FileAccess.Read, "", ""));

            foreach (var disk in disks)
            {
                CheckChild(disk as DiscUtils.Vhd.Disk);
            }
            
        }

        [Fact]
        public void TestAutomatlLocator()
        {
            var logger = new LogWatcher();

            CheckChild(DiskImage.Vhd.Util.OpenDiskWithDu(Path.Join(".", "testdata", "test_gpt_child.vhd"), logger));
            CheckChild(DiskImage.Vhd.Util.OpenDiskWithDu(Path.Join("testdata", "test_gpt_child.vhd"), logger));
            CheckChild(DiskImage.Vhd.Util.OpenDiskWithDu(Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"), logger));
        }

        private static void CheckChild(DiscUtils.Vhd.Disk? disk)
        {
            Assert.NotNull(disk);
            Assert.Equal(2, disk.Layers.Count());
        }
    }
}