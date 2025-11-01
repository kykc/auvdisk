using auvdisk.DiskImage.Vhd;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class CreateDiffVhdTest : IDisposable
    {
        private readonly string _parentPath = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt.vhd");
        private readonly string _childPath = Path.Join(Directory.GetCurrentDirectory(), "test_gpt_child_test.vhd");
        private readonly string _grandchildPath = Path.Join(Directory.GetCurrentDirectory(), "test_gpt_grandchild.vhd");
        
        [Fact]
        public void TestCreateDifferencingVhd()
        {
            Assert.False(File.Exists(_childPath));
            
            var logger = new LogWatcher();
            
            DiskImage.Vhd.Util.CreateDifferentialVhd(_parentPath, _childPath, logger);
            using var disk = DiskImage.Vhd.Util.OpenDiskWithDu(_childPath, logger);
            
            Assert.NotNull(disk);
            Assert.Equal(2, disk.Layers.Count());
            Assert.True(File.Exists(_childPath));
            Assert.True(Util.IsValidVhd(_childPath));
        }

        [Fact]
        public void TestTripleLayerVhd()
        {
            Assert.False(File.Exists(_grandchildPath));
            
            var logger = new LogWatcher();
            
            DiskImage.Vhd.Util.CreateDifferentialVhd(Path.Join("testdata", "test_gpt_child.vhd"), _grandchildPath, logger);
            using var disk = DiskImage.Vhd.Util.OpenDiskWithDu(_grandchildPath, logger);

            Assert.NotNull(disk);
            Assert.Equal(3, disk.Layers.Count());
            Assert.True(File.Exists(_grandchildPath));
            Assert.True(Util.IsValidVhd(_grandchildPath));
        }

        public void Dispose()
        {
            File.Delete(_childPath);
            File.Delete(_grandchildPath);
        }
    }
}