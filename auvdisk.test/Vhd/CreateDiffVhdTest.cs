namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class CreateDiffVhdTest : IDisposable
    {
        private readonly string _parentPath = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt.vhd");
        private readonly string _childPath = Path.Join(Directory.GetCurrentDirectory(), "test_gpt_child_test.vhd");
        
        [Fact]
        public void TestCreateDifferencingVhd()
        {
            Assert.False(File.Exists(_childPath));
            
            var logger = new LogWatcher();
            
            auvdisk.Vhd.Util.CreateDifferentialVhd(_parentPath, _childPath, logger);
            using var disk = auvdisk.Vhd.Util.OpenDiskWithDu(_childPath, logger);
            
            Assert.NotNull(disk);
            Assert.Equal(2, disk.Layers.Count());
            Assert.True(File.Exists(_childPath));
        }

        public void Dispose()
        {
            File.Delete(_childPath);
        }
    }
}