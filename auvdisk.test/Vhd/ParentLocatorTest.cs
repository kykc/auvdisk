using System.Runtime.InteropServices;
using auvdisk.Extensions;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class ParentLocatorTest
    {
        [Fact]
        public void TestDiscUtilsLocator()
        {
            var logger = new LogWatcher();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) // TODO: generalize to all posix?
            {
                // Relative with dot
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk("./testdata/test_gpt_child.vhd", FileAccess.Read));
                // Relative w/o dot
                CheckChild(Flows.Val(new DiscUtils.Vhd.Disk("testdata/test_gpt_child.vhd", FileAccess.Read)));
                // Absolute
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"), FileAccess.Read));
            }

            // NOTE: those tests would fail if you place your working copy in D:\automatl\auvdisk
            // because then absolute locators would actually point to the right file
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Relative with dot
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(@".\testdata\test_gpt_child.vhd", FileAccess.Read));
                // Relative w/o dot
                CheckChild(Flows.Val(new DiscUtils.Vhd.Disk(@"testdata\test_gpt_child.vhd", FileAccess.Read)));
                // Absolute
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(Path.Join(Directory.GetCurrentDirectory(), @"testdata\test_gpt_child.vhd"), FileAccess.Read));
            }
        }

        [Fact]
        public void TestDiscUtilsWithFactory()
        {
            var logger = new LogWatcher();
            
            List<string> paths =
            [
                Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"),
                Path.Join(".", "testdata", "test_gpt_child.vhd"),
                Path.Join("testdata", "test_gpt_child.vhd")
            ];

            var disks = paths.Select(p => DiscUtils.VirtualDisk.OpenDisk(p, "vhd", FileAccess.Read, "", ""));

            foreach (var disk in disks)
            {
                var maybeVhd = disk as DiscUtils.Vhd.Disk;
                CheckChild(Flows.Val(None.Value).MapOr(_ => maybeVhd, "Disk object is null"));
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

        private static void CheckChild(Flow<DiscUtils.Vhd.Disk> disk)
        {
            Assert.NotNull(disk);
            Assert.False(disk.IsErr);
            Assert.Equal(2, disk.UnwrapVal().Layers.Count());
        }
    }
}