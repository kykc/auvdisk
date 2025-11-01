using System.Runtime.InteropServices;
using auvdisk.DiskImage.Vhd;
using auvdisk.Log;
using DiscUtils.Fat;
using DiskAccessLibrary;
using DiscUtils;

namespace auvdisk.test.Vhd
{
    public class CreateResizeFixedVhdTest : IDisposable
    {
        private readonly string _targetCreateFast =
            Path.Join(Directory.GetCurrentDirectory(), "test_create_fixed_fast.vhd");
        private readonly string _targetCreateZeroFill =
            Path.Join(Directory.GetCurrentDirectory(), "test_create_fixed_zerofill.vhd");

        [Fact]
        public void TestCreateResizeFast()
        {
            Log.ILog logger = new LogWatcher();

            Assert.False(File.Exists(_targetCreateFast));

            if (!Environment.IsPrivilegedProcess && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw Xunit.Sdk.SkipException.ForSkip("This test requires administrator privileges on Windows platform");
            }

            ulong size = 1024UL * 1024UL * 1024UL * 10; // 10 GiB

            var disk = auvdisk.DiskImage.Vhd.Util.CreateFixedVhd(_targetCreateFast, size, logger, false);

            Assert.NotNull(disk);
            Assert.Equal(size, (ulong)disk.Size);
            Assert.True(Util.IsValidVhd(_targetCreateFast));

            WriteTestData(disk, size, logger);

            ulong newsize = 1024UL * 1024UL * 1024UL * 15; // 15 GiB

            var resizedDisk = auvdisk.DiskImage.Vhd.Util.ResizeFixedVhd(_targetCreateFast, newsize, logger, false);

            Assert.NotNull(resizedDisk);
            Assert.Equal(newsize, (ulong)resizedDisk.Size);
            Assert.True(Util.IsValidVhd(_targetCreateFast));

            CheckTestData(_targetCreateFast, logger);
        }

        [Fact]
        public void TestCreateResizeZeroFill()
        {
            Log.ILog logger = new LogWatcher();

            Assert.False(File.Exists(_targetCreateZeroFill));

            ulong size = 1024UL * 1024UL * 512; // 512 MiB

            var disk = auvdisk.DiskImage.Vhd.Util.CreateFixedVhd(_targetCreateZeroFill, size, logger, true);

            Assert.NotNull(disk);
            Assert.Equal(size, (ulong)disk.Size);

            WriteTestData(disk, size, logger);

            ulong newsize = 1024UL * 1024UL * 1024UL; // 1 GiB

            var resizedDisk = auvdisk.DiskImage.Vhd.Util.ResizeFixedVhd(_targetCreateZeroFill, newsize, logger, true);

            Assert.NotNull(resizedDisk);
            Assert.Equal(newsize, (ulong)resizedDisk.Size);

            CheckTestData(_targetCreateZeroFill, logger);
        }

        private void WriteTestData(VirtualHardDisk disk, ulong size, ILog logger)
        {
            const ulong offsetLba = 2048UL; // Start first partition from the sector/LBA 2048, this is what Windows does AFAIK

            List<GuidPartitionEntry> list = new List<GuidPartitionEntry>();

            GuidPartitionEntry bootPartitionEntry = new GuidPartitionEntry
            {
                PartitionGuid = Guid.NewGuid(),
                PartitionTypeGuid = GPTPartition.BasicDataPartititionTypeGuid,
                FirstLBA = offsetLba,
                LastLBA = size / auvdisk.DiskImage.Vhd.Util.LbaSize - 1,
                PartitionName = "Boot"
            };

            list.Add(bootPartitionEntry);

            DiskAccessLibrary.GuidPartitionTable.InitializeDisk(disk, (long)offsetLba, list);

            using var duDisk = new DiscUtils.Vhd.Disk(disk.Path, FileAccess.ReadWrite);

            logger.Log("Formatting target into FAT32");
            var fat = FatFileSystem.FormatPartition(duDisk, 0, "Boot");
            fat.CreateDirectory(@"EFI");
            fat.CreateDirectory(@"EFI\Boot");
            using var fileStream = fat.OpenFile(@"EFI\Boot\test.txt", FileMode.CreateNew);
            var streamWriter = new StreamWriter(fileStream);
            streamWriter.WriteLine("Hello World");
            streamWriter.Flush();
            streamWriter.Close();
        }

        private void CheckTestData(string target, ILog logger)
        {
            var probeResult = new auvdisk.DiskImage.DiskProbe(target, logger, FsHandler).Probe();

            Assert.NotNull(probeResult.Disk);

            void FsHandler(DiscFileSystem fs)
            {
                Assert.True(fs is FatFileSystem);

                using var stream = fs.OpenFile(@"\EFI\Boot\test.txt", FileMode.Open, FileAccess.Read);

                var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                Assert.Equal("Hello World", text.TrimEnd('\n', '\r'));
            }
        }

        public void Dispose()
        {
            File.Delete(_targetCreateFast);
            File.Delete(_targetCreateZeroFill);
        }
    }
}