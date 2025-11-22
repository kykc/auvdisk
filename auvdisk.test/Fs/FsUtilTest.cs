using auvdisk.DiskImage;

namespace auvdisk.test.Fs
{
    [Collection("Sequential")]
    public class FsUtilTest : IDisposable
    {
        [Fact]
        public void TestFileSegmentExtract()
        {
            var logger = new LogWatcher();

            var sourceLoop = Path.Join("testdata", "ext4.loop");
            var intermediateVhd = "ext4.vhd";
            var resultLoop = "ext4_result.loop";
            
            Assert.False(File.Exists(intermediateVhd));
            Assert.False(File.Exists(resultLoop));
            
            DiskImageConverter.ConvertLoopToVhd(sourceLoop, intermediateVhd, logger, false, true);
            
            Assert.True(File.Exists(intermediateVhd));
            
            auvdisk.Fs.Util.ExtractFileSegment(intermediateVhd, resultLoop,  537919488, 6274560, logger);

            Assert.True(File.Exists(resultLoop));

            var probeResult = new DiskProbe(resultLoop, logger).Probe();

            Assert.NotNull(probeResult.Fs);
            Assert.Null(probeResult.Disk);
            Assert.Equal("EXT", probeResult.Fs.FsType);
        }

        public void Dispose()
        {
            File.Delete("ext4.vhd");
            File.Delete("ext4_result.loop");
        }
    }
}