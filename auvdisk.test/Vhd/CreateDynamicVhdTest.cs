using auvdisk.DiskImage.Vhd;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class CreateDynamicVhdTest : IDisposable
    {
        private readonly string _targetPath = Path.Join(Directory.GetCurrentDirectory(), "test_dynamic.vhd");

        [Fact]
        public void TestCreateDynamicVhd()
        {
            var logger = new LogWatcher();
            
            Assert.False(File.Exists(_targetPath));

            // Test with size which is not divisible nor by 512 neither by 2MiB block
            DiskImage.Vhd.Util.CreateDynamicVhd(_targetPath, 107374182401UL, logger);
            using var disk = DiskImage.Vhd.Util.OpenDiskWithDu(_targetPath, logger);
            
            Assert.NotNull(disk);
            Assert.False(disk.IsError());
            Assert.Single(disk.Unwrap().Layers);
            Assert.True(File.Exists(_targetPath));
            Assert.True(Util.IsValidVhd(_targetPath));
        }

        public void Dispose()
        {
            File.Delete(_targetPath);
        }
    }
}