using auvdisk.DiskImage;
using auvdisk.Extensions;
using auvdisk.Log;
using DiskAccessLibrary;

namespace auvdisk.test.Vhd
{
    [Collection("Sequential")]
    public class VhdUtilTest : IDisposable
    {
        const ulong BootSize = 64 * 1024 * 1024;
        const ulong DataSize = 32 * 1024 * 1024 + 1;
        
        private string TargetFixed => "vhdutil_bootable_fixed.vhd";
        private string TargetDynamic => "vhdutil_bootable_dynamic.vhd";

        private ILog Logger { get; } = new LogWatcher();
        
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

        [Fact]
        public void TestCreateBootableVhdLayout()
        {
            Assert.False(File.Exists(TargetFixed));
            Assert.False(File.Exists(TargetDynamic));
            
            var resultFixed = DiskImage.Vhd.Util.CreateBootableVhdLayout(TargetFixed, BootSize, DataSize, Logger, false, false);
            var probeFixed = new DiskProbe(TargetFixed, Logger).Probe();
            
            CheckLayout(resultFixed, probeFixed);
            
            var resultDynamic = DiskImage.Vhd.Util.CreateBootableVhdLayout(TargetDynamic, BootSize, DataSize, Logger, false, true);
            var probeDynamic = new DiskProbe(TargetDynamic, Logger).Probe();
            
            CheckLayout(resultDynamic, probeDynamic);
            Assert.True(GetLength(TargetDynamic) < (long)DataSize);
        }
        
        private long GetLength(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return file.Length;
        }

        private void CheckLayout(Flow<Value<ulong>> layoutResult, DiskProbe.ProbeResult probeResult)
        {
            Assert.False(layoutResult.IsError());
            Assert.Equal(DataSize + 511, layoutResult.Unwrap().Val);
            
            
            Assert.NotNull(probeResult.Disk);
            Assert.Equal(2, probeResult.Disk.Partitions.Count);
            Assert.False(probeResult.Disk.Partitions[0].FileSystem.HasValue);
            Assert.False(probeResult.Disk.Partitions[1].FileSystem.HasValue);
            Assert.Equal(2048, probeResult.Disk.Partitions[0].StartLba);
        }

        public void Dispose()
        {
            File.Delete(TargetDynamic);
            File.Delete(TargetFixed);
        }
    }
}