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
            
            auvdisk.Vhd.Util.CreateDynamicVhd(_targetPath, 1024 * 1024 * 1024, logger);
            using var disk = auvdisk.Vhd.Util.OpenDiskWithDu(_targetPath, logger);
            
            Assert.NotNull(disk);
            Assert.Single(disk.Layers);
            Assert.True(File.Exists(_targetPath));
        }

        public void Dispose()
        {
            File.Delete(_targetPath);
        }
    }
}