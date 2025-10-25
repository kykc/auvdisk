using auvdisk.DiskImage;
using DiscUtils;

namespace auvdisk.test;

[Collection("Sequential")]
public class VhdMergeTest : IDisposable
{
    [Fact]
    public void TestVhdMerge()
    {
        Assert.False(File.Exists("test_gpt_merged.vhd"));

        var logger = new LogWatcher();
        
        var fsHandler = (DiscFileSystem fs) =>
        {
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
        
        var parentPath = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt.vhd");
        var childPath = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd");

        var targetPath = "test_gpt_merged.vhd";
        
        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath, targetPath, logger, true))
        {
            var resultDisk = result.Unwrap();

            Assert.Single(resultDisk.Layers);

            var probeResult = new DiskProbe(targetPath, logger, fsHandler).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResult.Disk.ImageType);
            Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
            Assert.Single(probeResult.Disk.Partitions);
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath, targetPath, logger, true))
        {
            Assert.Throws<NullReferenceException>(() => result.Unwrap());
            Assert.Equal($"Target image {targetPath} already exists", result.UnwrapErr());
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath + "bork", childPath, targetPath, logger, true))
        {
            Assert.Throws<NullReferenceException>(() => result.Unwrap());
            Assert.Equal($"{parentPath}bork does not exist", result.UnwrapErr());
        }

        using (var result = DiskImage.Vhd.Merge.PerformMerge(parentPath, childPath + "bork", targetPath, logger, true))
        {
            Assert.Throws<NullReferenceException>(() => result.Unwrap());
            Assert.Equal($"{childPath}bork does not exist", result.UnwrapErr());
        }
    }

    public void Dispose()
    {
        File.Delete("test_gpt_merged.vhd");
    }
}
