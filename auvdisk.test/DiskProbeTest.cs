using DiscUtils;
using auvdisk.DiskImage;

namespace auvdisk.test;

[Collection("Sequential")]
public class DiskProbeTest
{
    [Fact]
    public void TestNoSideEffects()
    {
        var logger = new LogWatcher();

        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");
        var subjectLoop = Path.Join("testdata", "ext4.loop");

        var loopHashBefore = TestUtil.CalcSha256Hash(subjectLoop);
        var vhdHashBefore = TestUtil.CalcSha256Hash(subjectVhd);

        var loopProbeResult = new DiskProbe(subjectLoop, logger).Probe();
        var vhdProbeResult = new DiskProbe(subjectVhd, logger).Probe();
        
        var loopHashAfter = TestUtil.CalcSha256Hash(subjectLoop);
        var vhdHashAfter = TestUtil.CalcSha256Hash(subjectVhd);
        
        Assert.Equal(loopHashBefore, loopHashAfter);
        Assert.Equal(vhdHashBefore, vhdHashAfter);
    }

    [Fact]
    public void TestDifferencingVhd()
    {
        var logger = new LogWatcher();
        
        var subjectVhd = Path.Join(Directory.GetCurrentDirectory(), "testdata", "test_gpt_child.vhd");

        var fsHandler = (DiscFileSystem fs) =>
        {
            using (var stream = fs.OpenFile(@"\child_text.txt", FileMode.Open, FileAccess.Read))
            {
                var streamReader = new StreamReader(stream);
                var text = streamReader.ReadToEnd();
                Assert.Equal("child_text", text);
            }
        };
        
        var probeResult = new DiskProbe(subjectVhd, logger, fsHandler).Probe();
        
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
        var logger = new LogWatcher();
        
        var subjectVhd = Path.Join("testdata", "dynamic_fat32.vhd");
        
        var probeResult = new DiskProbe(subjectVhd, logger).Probe();
        
        Assert.NotNull(probeResult.Disk);
        Assert.Equal(2, probeResult.Disk.Partitions.Count);
        Assert.True(probeResult.Disk.Partitions[1].FileSystem.HasValue);
        Assert.Equal("FAT", probeResult.Disk.Partitions[1].FileSystem.Value.FsType);
        Assert.Equal("B4A3-3F88",  probeResult.Disk.Partitions[1].FileSystem.Value.VolumeId);
    }

    [Fact]
    public void TestExtLoop()
    {
        var logger = new LogWatcher();
        var subjectLoop = Path.Join("testdata", "ext4.loop");
        
        var probeResult =  new DiskProbe(subjectLoop, logger).Probe();
        
        Assert.NotNull(probeResult.Fs);
        Assert.Equal("EXT", probeResult.Fs.FsType);
        Assert.Equal("2c9e6984-a7e3-4632-bb69-45d9a78f0ea1", probeResult.Fs.VolumeId);
    }

    [Fact]
    public void TestListDirectory()
    {
        var logger = new LogWatcher();
        var fsLogger = new LogWatcher();
        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");

        var fsHandler = DiskProbe.GetListArbitraryDir(@"\test_dir", fsLogger, true);

        var probeResult = new DiskProbe(subjectVhd, logger, fsHandler).Probe();
        
        Assert.NotNull(probeResult.Disk);

        // test_dir is empty
        Assert.Empty(fsLogger.GetAll());
    }

    [Fact]
    public void TestCatFile()
    {
        var logger = new LogWatcher();
        var subjectVhd = Path.Join("testdata", "test_gpt.vhd");
        var fsLogger = new LogWatcher();

        var fsHandler = DiskProbe.GetCatArbitraryFile(@"\test_text.txt", fsLogger, true);

        var probeResult = new DiskProbe(subjectVhd, logger, fsHandler).Probe();
        
        Assert.NotNull(probeResult.Disk);
        var fsLog = fsLogger.GetAll().ToList();
        Assert.Single(fsLog);
        Assert.Equal("test_text", fsLog.First());
    }
}
