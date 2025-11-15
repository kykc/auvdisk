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
                CheckChild(Flows.Ok(new DiscUtils.Vhd.Disk("testdata/test_gpt_child.vhd", FileAccess.Read), logger));
                // Absolute
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd"), FileAccess.Read));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Relative with dot
                Assert.Throws<IOException>(() => new DiscUtils.Vhd.Disk(@".\testdata\test_gpt_child.vhd", FileAccess.Read));
                // Relative w/o dot
                CheckChild(Flows.Ok(new DiscUtils.Vhd.Disk(@"testdata\test_gpt_child.vhd", FileAccess.Read), logger));
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
                CheckChild(Flows.Ok(None.Value, logger).MapOr(_ => maybeVhd, "Disk object is null"));
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
            Assert.False(disk.IsError());
            Assert.Equal(2, disk.Unwrap().Layers.Count());
        }
    }
}