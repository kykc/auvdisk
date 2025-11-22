using auvdisk.DiskImage;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;

namespace auvdisk.test;

public class VhdMergeTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    
    public VhdMergeTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    // I'm not entirely sure how aggressive is xUnit's parallelization of different UTs, hence thread-safe collection. 
    private readonly System.Collections.Concurrent.ConcurrentBag<string> _createdFiles = [];
    
    private readonly string _fixedParent = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt.vhd");
    private readonly string _fixedChild = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd");
    //private readonly string _dynamicParent = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dynamic_fat32.vhd");
    private readonly string _dynamicChild = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dynamic_fat32_child.vhd");
    private readonly string _dynamicGrandchild = Path.Join(Directory.GetCurrentDirectory(), "testdata", "dynamic_fat32_grandchild.vhd");

    [Fact]
    public void TestVhdMergeDifferencingParent()
    {
        var logger = new LogWatcher();

        PerformTest(_dynamicGrandchild, false, "TestVhdMergeDifferencing", 2, logger);
    }
    
    [Fact]
    public void TestVhdMergeDifferencingParentInPlace()
    {
        var logger = new LogWatcher();
        
        PerformTest(_dynamicGrandchild, true, "TestVhdMergeDifferencingInPlace", 2, logger);
    }
    
    [Fact]
    public void TestVhdMergeDynamicParentInPlace()
    {
        var logger = new LogWatcher();
        
        PerformTest(_dynamicChild, true, "TestVhdMergeDynamicParentInPlace", 2, logger);
    }
    
    [Fact]
    public void TestVhdMergeFixedParentInPlace()
    {
        var logger = new LogWatcher();
        PerformTest(_fixedChild, true, "TestVhdMergeFixedParentInPlace", 1, logger);
    }
    
    [Fact]
    public void TestVhdMergeDynamicParent()
    {
        var logger = new LogWatcher();

        void FsHandler(DiscFileSystem fs)
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
        }

        PerformTest(_dynamicChild, false, "TestVhdMergeDynamicParent", 2, logger, FsHandler);
    }
    
    [Fact]
    public void TestVhdMergeFixedParent()
    {
        var logger = new LogWatcher();

        void FsHandler(DiscFileSystem fs)
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
        }
        
        var createdFiles = PerformTest(_fixedChild, false, "TestVhdMergeFixedParent", 1, logger, FsHandler);

        Assert.True(createdFiles.Count > 1);
        
        var parentPath = _fixedParent;
        var childPath = _fixedChild;
        var targetPath = createdFiles.Last();
        
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

    private List<string> PerformTest(string targetPath, bool inPlace, string prefix, int expectedPartitionCount, ILog logger, Action<DiscFileSystem>? action = null)
    {
        var cloneResult = TestUtil.CloneVhdChain(targetPath, Directory.GetCurrentDirectory(), prefix, logger);
        
        Assert.False(cloneResult.IsErr);
        var createdFiles = cloneResult.UnwrapVal();
        createdFiles.ForEach(x => _createdFiles.Add(x));
        
        Assert.True(createdFiles.Count > 1);

        var targetChild = createdFiles.AsEnumerable().Reverse().First();
        var targetParent = createdFiles.AsEnumerable().Reverse().Skip(1).First();
        var newFile = $"{prefix}_merged.vhd";
        var resultFile = inPlace ? targetParent : newFile;

        using (var result = DiskImage.Vhd.Merge.PerformMerge(targetParent, targetChild, resultFile, logger))
        {
            Assert.False(result.IsErr);
            using var targetDisk = DiskImage.Vhd.Util.OpenDiskWithDu(targetPath, logger);
            Assert.False(targetDisk.IsErr);
            Utils.If(() => !inPlace, () => _createdFiles.Add(newFile));
            
            Assert.Equal(TestUtil.LazyFastDiskHash(targetDisk.UnwrapVal()), TestUtil.LazyFastDiskHash(result.UnwrapVal()));
        }
        
        var probeResult = new DiskProbe(resultFile, logger, action).Probe();

        Assert.NotNull(probeResult.Disk);
        Assert.Equal("VHD", probeResult.Disk.ImageType);
        Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
        Assert.Equal(expectedPartitionCount, probeResult.Disk.Partitions.Count);
        
        return createdFiles;
    }

    public void Dispose()
    {
        _createdFiles.ForEach(File.Delete);
        GC.SuppressFinalize(this);
    }
}
