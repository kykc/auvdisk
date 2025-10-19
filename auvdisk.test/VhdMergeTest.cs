using System.Net;
using DiscUtils;

namespace auvdisk.test;

public class VhdMergeTest : IDisposable
{
    [Fact]
    public void TestVhdMerge()
    {
        Assert.False(File.Exists("test_gpt_merged.vhd"));
        
        var logger = (string s) => { };
        
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
        
        var parentPath = Path.Join("testdata", "test_gpt.vhd");
        var childPath = Path.Join("testdata", "test_gpt_child.vhd");

        var targetPath = "test_gpt_merged.vhd";
        
        auvdisk.Vhd.Merge.PerformMerge(parentPath, childPath, targetPath, logger, true);

        var probeResult = new DiskProbe(targetPath, 0, 0, fsHandler, logger).Probe();

        Assert.NotNull(probeResult.Disk);
        Assert.Equal("VHD", probeResult.Disk.ImageType);
        Assert.Equal("GPT", probeResult.Disk.PartitionTableType);
        Assert.Single(probeResult.Disk.Partitions);
    }

    public void Dispose()
    {
        File.Delete("test_gpt_merged.vhd");
    }
}
