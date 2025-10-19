using DiscUtils;

namespace auvdisk.test;

[Collection("Sequential")]
public class DiskProbeTest
{
    [Fact]
    public void TestNoSideEffects()
    {
        var logger = (string s) => { };

        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");
        var subjectLoop = Path.Join("testdata", "ext4.loop");

        var loopHashBefore = TestUtil.CalcSha256Hash(subjectLoop);
        var vhdHashBefore = TestUtil.CalcSha256Hash(subjectVhd);

        var loopProbeResult = new DiskProbe(subjectLoop, 0, 0, null, logger).Probe();
        var vhdProbeResult = new DiskProbe(subjectVhd, 0, 0, null, logger).Probe();
        
        var loopHashAfter = TestUtil.CalcSha256Hash(subjectLoop);
        var vhdHashAfter = TestUtil.CalcSha256Hash(subjectVhd);
        
        Assert.Equal(loopHashBefore, loopHashAfter);
        Assert.Equal(vhdHashBefore, vhdHashAfter);
    }

    [Fact]
    public void TestDifferencingVhd()
    {
        var logger = (string s) => { };
        
        var subjectVhd = Path.Join("testdata", "test_gpt_child.vhd");

        var fsHandler = (DiscFileSystem fs) =>
        {
            using (var stream = fs.OpenFile(@"\child_text.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var text = streamReader.ReadToEnd();
                Assert.Equal("child_text", text);
            }
        };
        
        var probeResult = new DiskProbe(subjectVhd, 0, 0, fsHandler, logger).Probe();
        
        Assert.NotNull(probeResult.Disk);
        Assert.Equal("VHD", probeResult.Disk.ImageType);
        Assert.Single(probeResult.Disk.Partitions);
        Assert.True(probeResult.Disk.Partitions[0].FileSystem.HasValue);
        Assert.Equal("NTFS",  probeResult.Disk.Partitions[0].FileSystem.Value.FsType);
        Assert.Equal("423CA96F3CA95F23", probeResult.Disk.Partitions[0].FileSystem.Value.VolumeId);
    }

    [Fact]
    public void TestDynamicVhd()
    {
        var logger = (string s) => { };
        
        var subjectVhd = Path.Join("testdata", "dynamic_fat32.vhd");
        
        var probeResult = new DiskProbe(subjectVhd, 0, 0, null, logger).Probe();
        
        Assert.NotNull(probeResult.Disk);
        Assert.Equal(2, probeResult.Disk.Partitions.Count);
        Assert.True(probeResult.Disk.Partitions[1].FileSystem.HasValue);
        Assert.Equal("FAT32", probeResult.Disk.Partitions[1].FileSystem.Value.FsType);
        Assert.Equal("B4A3-3F88",  probeResult.Disk.Partitions[1].FileSystem.Value.VolumeId);
    }

    [Fact]
    public void TestExtLoop()
    {
        var logger = (string s) => { };
        var subjectLoop = Path.Join("testdata", "ext4.loop");
        
        var probeResult =  new DiskProbe(subjectLoop, 0, 0, null, logger).Probe();
        
        Assert.NotNull(probeResult.Fs);
        Assert.Equal("EXT", probeResult.Fs.FsType);
        Assert.Equal("2c9e6984-a7e3-4632-bb69-45d9a78f0ea1", probeResult.Fs.VolumeId);
    }

    [Fact]
    public void TestListDirectory()
    {
        string log = "";
        var logger = (string s) => { log += $"{s}\n"; };
        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");

        var fsHandler = DiskProbe.GetListArbitraryDir(@"\test_dir", logger);

        var probeResult = new DiskProbe(subjectVhd, 0, 0, fsHandler, logger).Probe();
        
        Assert.NotNull(probeResult.Disk);

        // I hate this but there's not much else I can do w/o rather intensive refactoring
        var indexedLogLines = log.Split("\n").Select((line, idx) => (line, idx)).ToList();
        var line = indexedLogLines.First(x => x.line.StartsWith("Listing contents"));

        var remainder = indexedLogLines.Where(x => x.idx > line.idx);
        
        // Basically this checks that there was no errors after listing attempt
        Assert.DoesNotContain(remainder, x => x.line.StartsWith("ERROR"));
    }

    [Fact]
    public void TestCatFile()
    {
        string log = "";
        var logger = (string s) => { log += $"{s}\n"; };
        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");

        var fsHandler = DiskProbe.GetCatArbitraryFile(@"\test_text.txt", logger);

        var probeResult = new DiskProbe(subjectVhd, 0, 0, fsHandler, logger).Probe();
        
        Assert.NotNull(probeResult.Disk);

        // I hate this but there's not much else I can do w/o rather intensive refactoring
        Assert.Contains("test_text", log.Split("\n"));
    }
}
