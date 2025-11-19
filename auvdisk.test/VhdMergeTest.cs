using auvdisk.DiskImage;
using DiscUtils;

namespace auvdisk.test;

[Collection("Sequential")]
public class VhdMergeTest : IDisposable
{
    private readonly string _fixedParent = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt.vhd");
    private readonly string _fixedChild = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd");
    private readonly string _fixedTargetInPlace = "test_gpt_merged_in_place.vhd";
    private readonly string _dynamicParent = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dynamic_fat32.vhd");
    private readonly string _dynamicChild = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dynamic_fat32_child.vhd");
    private readonly string _dynamicTarget = "dynamic_fat32_child_merged.vhd";
    private readonly string _dynamicTargetInPlace = "dynamic_fat32_child_merged_inplace.vhd";

    [Fact]
    public void TestVhdMergeDynamicParentInPlace()
    {
        Assert.False(File.Exists(_dynamicTargetInPlace));
        
        var logger = new LogWatcher();
        
        File.Copy(_dynamicParent, _dynamicTargetInPlace);
        
        using (var result = DiskImage.Vhd.Merge.PerformMerge(_dynamicTargetInPlace, _dynamicChild, _dynamicTargetInPlace, logger))
        {
            Assert.False(result.IsErr);
            var resultDisk = result.UnwrapVal();

            Assert.Single(resultDisk.Layers);
            resultDisk.Dispose();

            var probeResult = new DiskProbe(_dynamicTargetInPlace, logger, null).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResult.Disk.ImageType);
            Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
            Assert.Equal(2, probeResult.Disk.Partitions.Count);
        }
        
        using var referenceDisk = DiscUtils.VirtualDisk.OpenDisk(_dynamicChild, FileAccess.Read);
        // I reopen disk on purpose, to be sure that all content is being read from FS as I'm not 100% aware of what DU might be doing internally
        using var resultDiskReopened = DiscUtils.VirtualDisk.OpenDisk(_dynamicTargetInPlace, FileAccess.Read);
        
        Assert.Equal(TestUtil.LazyFastDiskHash(referenceDisk), TestUtil.LazyFastDiskHash(resultDiskReopened));
    }

    [Fact]
    public void TestFastHash()
    {
        using var stream1 = new MemoryStream();
        using var stream2 = new MemoryStream();
        using var stream3 = new MemoryStream();
        
        var writer1 = new StreamWriter(stream1);
        var writer2 = new StreamWriter(stream2);
        var writer3 = new StreamWriter(stream3);
        
        writer1.WriteLine("identical data");
        writer2.WriteLine("identical data");
        writer3.WriteLine("different data");
        
        writer1.Flush();
        writer2.Flush();
        writer3.Flush();

        Assert.Equal(TestUtil.CalculateAdler32([stream1]), TestUtil.CalculateAdler32([stream2]));
        Assert.NotEqual(TestUtil.CalculateAdler32([stream1]), TestUtil.CalculateAdler32([stream3]));
        Assert.Equal(TestUtil.CalculateAdler32([stream1, stream2]), TestUtil.CalculateAdler32([stream2, stream1]));
    }
    
    [Fact]
    public void TestVhdMergeFixedParentInPlace()
    {
        Assert.False(File.Exists(_fixedTargetInPlace));
        
        var logger = new LogWatcher();
        
        File.Copy(_fixedParent, _fixedTargetInPlace);
        
        using (var result = DiskImage.Vhd.Merge.PerformMerge(_fixedTargetInPlace, _fixedChild, _fixedTargetInPlace, logger))
        {
            Assert.False(result.IsErr);
            var resultDisk = result.UnwrapVal();

            Assert.Single(resultDisk.Layers);
            resultDisk.Dispose();

            var probeResult = new DiskProbe(_fixedTargetInPlace, logger, null).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResult.Disk.ImageType);
            Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
            Assert.Single(probeResult.Disk.Partitions);
        }
        
        using var referenceDisk = DiscUtils.VirtualDisk.OpenDisk(_fixedChild, FileAccess.Read);
        // I reopen disk on purpose, to be sure that all content is being read from FS as I'm not 100% aware of what DU might be doing internally
        using var resultDiskReopened = DiscUtils.VirtualDisk.OpenDisk(_fixedTargetInPlace, FileAccess.Read);
        
        Assert.Equal(TestUtil.LazyFastDiskHash(referenceDisk), TestUtil.LazyFastDiskHash(resultDiskReopened));
    }
    
    [Fact]
    public void TestVhdMergeDynamicParent()
    {
        Assert.False(File.Exists(_dynamicTarget));
        
        var fsHandler = (DiscFileSystem fs) =>
        {
            Assert.True(fs.FileExists(@"\child_dir\child.txt"));
            Assert.True(fs.FileExists(@"\test_dir\test_text.txt"));
            
            using (var stream = fs.OpenFile(@"\child_dir\child.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var text = streamReader.ReadToEnd();
                
                Assert.Equal("child_test_text", text);
            }

            using (var stream = fs.OpenFile(@"\test_dir\test_text.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var testText = streamReader.ReadToEnd();
                
                Assert.Equal("test_text", testText);
            }
        } ;
        
        var logger = new LogWatcher();
        
        using (var result = DiskImage.Vhd.Merge.PerformMerge(_dynamicParent, _dynamicChild, _dynamicTarget, logger))
        {
            Assert.False(result.IsErr);
            var resultDisk = result.UnwrapVal();

            Assert.Single(resultDisk.Layers);
            resultDisk.Dispose();

            var probeResult = new DiskProbe(_dynamicTarget, logger, fsHandler).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResult.Disk.ImageType);
            Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
            Assert.Equal(2, probeResult.Disk.Partitions.Count);

            using var resultReopened = DiscUtils.VirtualDisk.OpenDisk(_dynamicTarget, FileAccess.Read);
            using var reference = DiscUtils.VirtualDisk.OpenDisk(_dynamicChild, FileAccess.Read);
            Assert.Equal(TestUtil.LazyFastDiskHash(reference), TestUtil.LazyFastDiskHash(resultReopened));
        }
    }
    
    [Fact]
    public void TestVhdMergeFixedParent()
    {
        Assert.False(File.Exists("test_gpt_merged.vhd"));

        var logger = new LogWatcher();
        
        var fsHandler = (DiscFileSystem fs) =>
        {
            Assert.True(fs.FileExists(@"\child_text.txt"));
            Assert.True(fs.FileExists(@"\test_text.txt"));
            
            using (var stream = fs.OpenFile(@"\child_text.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var text = streamReader.ReadToEnd();
                
                Assert.Equal("child_text", text);
            }

            using (var stream = fs.OpenFile(@"\test_text.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var testText = streamReader.ReadToEnd();
                
                Assert.Equal("test_text", testText);
            }
        } ;
        
        var parentPath = _fixedParent;
        var childPath = _fixedChild;

        var targetPath = "test_gpt_merged.vhd";
        
        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath, targetPath, logger))
        {
            Assert.False(result.IsErr);
            var resultDisk = result.UnwrapVal();

            Assert.Single(resultDisk.Layers);
            resultDisk.Dispose();

            var probeResult = new DiskProbe(targetPath, logger, fsHandler).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResult.Disk.ImageType);
            Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
            Assert.Single(probeResult.Disk.Partitions);
            
            using var reopenedResult = VirtualDisk.OpenDisk(targetPath, FileAccess.Read);
            using var reference = VirtualDisk.OpenDisk(childPath, FileAccess.Read);
            
            Assert.Equal(TestUtil.LazyFastDiskHash(reference), TestUtil.LazyFastDiskHash(reopenedResult));
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath, targetPath, logger))
        {
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());
            Assert.Equal($"Target image {targetPath} already exists", result.UnwrapErr());
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath + "bork", childPath, targetPath, logger))
        {
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());
            Assert.Equal($"{parentPath}bork does not exist", result.UnwrapErr());
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath + "bork", targetPath, logger))
        {
            Assert.Throws<NullReferenceException>(() => result.UnwrapVal());
            Assert.Equal($"{childPath}bork does not exist", result.UnwrapErr());
        }
    }

    public void Dispose()
    {
        File.Delete("test_gpt_merged.vhd");
        File.Delete(_dynamicTarget);
        File.Delete(_fixedTargetInPlace);
        File.Delete(_dynamicTargetInPlace);
    }
}
