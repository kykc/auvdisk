using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiskAccessLibrary.VHD;
using auvdisk.DiskImage;
using auvdisk.DiskImage.Vhd;
using DiscUtils.Streams;

namespace auvdisk.test;

[Collection("Sequential")]
public class DiskImageConverterTest : IDisposable
{
    [Fact]
    public void TestLoopToVhdAndBack()
    {
        var logger = new LogWatcher();

        var sep = Path.DirectorySeparatorChar;
        
        var fsHandler = (DiscFileSystem fs) =>
        {
            if (fs is FatFileSystem)
            {
                Assert.Single(fs.GetDirectories(""));
                Assert.Contains(fs.GetDirectories(""), x => x == "EFI");
            }
            else if (fs is ExtFileSystem)
            {
                Assert.Contains(fs.GetDirectories(""), x => x == @$"{sep}test_dir");
                
                Assert.Contains(fs.GetFiles(""), x => x == @$"{sep}test_root.txt");

                using (var stream = fs.OpenFile(@"\test_root.txt", FileMode.Open, FileAccess.Read))
                {
                    var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    Assert.Equal("test_root\n", text);
                }

                using (var stream = fs.OpenFile(@$"{sep}test_dir{sep}test_subdir.txt", FileMode.Open, FileAccess.Read))
                {
                    var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    Assert.Equal("test_subdir\n", text);
                }
            }
        };
        
        // Check that environment isn't contaminated from previous failed run
        Assert.False(File.Exists(@"ext4.vhd"));
        Assert.False(File.Exists(@"ext4.loop"));
        
        var resultForward = DiskImageConverter.ConvertLoopToVhd(Path.Join("testdata", "ext4.loop"), "ext4.vhd", logger, false, true);
        
        Assert.False(resultForward.IsErr);
        
        var probeResult = new DiskProbe("ext4.vhd", logger, fsHandler).Probe();
        
        Assert.NotNull(probeResult.Disk);
        Assert.Equal(2, probeResult.Disk.Partitions.Count);
        
        Assert.True(probeResult.Disk.Partitions[0].FileSystem.HasValue);
        Assert.Equal("FAT", probeResult.Disk.Partitions[0].FileSystem.Value.FsType);
        
        Assert.True(probeResult.Disk.Partitions[1].FileSystem.HasValue);
        Assert.Equal("EXT", probeResult.Disk.Partitions[1].FileSystem.Value.FsType);

        Assert.True(DiskImage.Vhd.Util.IsValidVhd("ext4.vhd"));
        
        var resultBack = DiskImageConverter.ConvertVhdToLoop("ext4.vhd", "ext4.loop", logger, false);
        
        Assert.False(resultBack.IsErr);
        
        probeResult = new DiskProbe("ext4.loop", logger, fsHandler).Probe();

        Assert.NotNull(probeResult.Fs);
        Assert.Equal("FAT", probeResult.Fs.FsType);

        File.Delete("ext4.loop");
        
        DiskImageConverter.ConvertVhdToLoop("ext4.vhd", "ext4.loop", logger, false, 1);
        probeResult = new DiskProbe("ext4.loop", logger, fsHandler).Probe();

        Assert.NotNull(probeResult.Fs);
        Assert.Equal("EXT", probeResult.Fs.FsType);
    }

    [Fact]
    public void TestVhdToImgAndBack()
    {
        string original = Path.Join("testdata", "test_gpt.vhd");
        string subject = "test_gpt_subject.vhd";

        var logger = new LogWatcher();

        var fsHandler = (DiscFileSystem fs) =>
        {
            Assert.Single(fs.GetDirectories(@""));
            Assert.Contains(fs.GetDirectories(@""), x => x == "test_dir");
            Assert.Single(fs.GetFiles(""));

            using var stream = fs.OpenFile(@"\test_text.txt", FileMode.Open, FileAccess.Read);
            var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
                
            Assert.Equal("test_text", text);
        };

        var probeResultHandler = (DiskProbe.ProbeResult probeResult, string imageType) =>
        {
            Assert.NotNull(probeResult.Disk);
            Assert.Equal(imageType, probeResult.Disk.ImageType);
            Assert.Single(probeResult.Disk.Partitions);
            Assert.True(probeResult.Disk.Partitions[0].FileSystem.HasValue);
            Assert.Equal("NTFS", probeResult.Disk.Partitions[0].FileSystem.Value.FsType);
        };
        
        Assert.False(File.Exists(subject));

        var originalVhdFooter = TestUtil.ReadVhdFooter(original);
        
        Assert.NotNull(originalVhdFooter);
        Assert.True(originalVhdFooter.IsValid);
        
        // As this operation is in place, always copy file to have the clean one untouched
        File.Copy(original, subject);

        DiskImageConverter.ConvertVhdToImg(subject, logger, false);
        
        var probe = new DiskProbe(subject, logger, fsHandler);
        probeResultHandler(probe.Probe(), "RAW");

        DiskImageConverter.ConvertImgToVhd(subject, logger, false);
        probeResultHandler(probe.Probe(), "VHD");

        Assert.Equal(new FileInfo(original).Length, new FileInfo(subject).Length);

        var resultVhdFooter = TestUtil.ReadVhdFooter(subject);

        Assert.NotNull(resultVhdFooter);
        Assert.True(resultVhdFooter.IsValid);
        Assert.Equal(originalVhdFooter.DiskType, resultVhdFooter.DiskType);
        Assert.Equal(VirtualHardDiskType.Fixed, resultVhdFooter.DiskType);
        Assert.Equal(originalVhdFooter.CurrentSize, resultVhdFooter.CurrentSize);
        Assert.Equal(originalVhdFooter.OriginalSize, resultVhdFooter.OriginalSize);
        Assert.True(DiskImage.Vhd.Util.IsValidVhd(subject));
    }
    
    [Fact]
    public void TestVhdToVhdxAndBack()
    {
        string original = Path.Join("testdata", "test_gpt.vhd");
        string originalDynamic = Path.Join("testdata", "dynamic_fat32.vhd");
        string target = Path.Join(Directory.GetCurrentDirectory(), "test_gpt.vhdx");
        string targetBack = Path.Join(Directory.GetCurrentDirectory(), "test_gpt_back.vhd");

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(targetBack));

        var logger = new LogWatcher();

        var fsHandler = (DiscFileSystem fs) =>
        {
            Assert.Single(fs.GetDirectories(@""));
            Assert.Contains(fs.GetDirectories(@""), x => x == "test_dir");
            Assert.Single(fs.GetFiles(""));

            using var stream = fs.OpenFile(@"\test_text.txt", FileMode.Open, FileAccess.Read);
            var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            Assert.Equal("test_text", text);
        };

        var testImpl = (bool fixedTarget) =>
        {
            File.Delete(target);

            var source = fixedTarget ? original : originalDynamic;
            
            var result = DiskImageConverter.ConvertVhdToVhdx(source, target, logger, false, fixedTarget, false);
            Assert.True(result.IsVal);

            var probeResult = new DiskProbe(target, logger, fsHandler).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHDX", probeResult.Disk.ImageType);
            Assert.Equal(fixedTarget ? 1 : 2, probeResult.Disk.Partitions.Count);

            File.Delete(targetBack);
            
            var resultBack = DiskImageConverter.ConvertVhdxToVhd(target, targetBack, logger, false, fixedTarget, false);
            Assert.True(resultBack.IsVal);

            var probeResultBack = new DiskProbe(targetBack, logger, fsHandler).Probe();

            Assert.NotNull(probeResult.Disk);
            Assert.Equal("VHD", probeResultBack.Disk!.ImageType);
            Assert.Equal(fixedTarget ? 1 : 2, probeResultBack.Disk.Partitions.Count);

            using var initialVhd = DiscUtils.VirtualDisk.OpenDisk(source, "vhd", FileAccess.Read, "", "");
            using var resultVhd = DiscUtils.VirtualDisk.OpenDisk(targetBack, "vhd", FileAccess.Read, "", "");

            Assert.Equal(TestUtil.LazyFastDiskHash(initialVhd), TestUtil.LazyFastDiskHash(resultVhd));
            
            Assert.True(DiskImage.Vhd.Util.IsValidVhd(targetBack));
        };

        testImpl(false);
        testImpl(true);
    }

    [Fact]
    public void TestQcow2ToRaw()
    {
        var logger = new LogWatcher();

        string sourcePath = Path.Join("testdata", "test_gpt.qcow2");
        string targetPath = Path.Join(Directory.GetCurrentDirectory(), "test_gpt_from_qcow2.img");
        string referencePath = Path.Join("testdata", "test_gpt.vhd");

        Assert.False(File.Exists(targetPath));

        var result = DiskImageConverter.ConvertQcow2ToRaw(sourcePath, targetPath, logger, false);

        Assert.True(result.IsVal);

        using var reference = DiscUtils.VirtualDisk.OpenDisk(referencePath, FileAccess.Read);
        using var resultDisk = DiscUtils.VirtualDisk.OpenDisk(targetPath, FileAccess.Read);
        Assert.Equal("RAW", resultDisk.DiskTypeInfo.Name);

        reference.Content.Seek(0, SeekOrigin.Begin);
        resultDisk.Content.Seek(0, SeekOrigin.Begin);

        var referenceHash = TestUtil.CalcSha256Hash(reference.Content);
        var resultHash = TestUtil.CalcSha256Hash(resultDisk.Content);

        Assert.Equal(reference.Content.Length, resultDisk.Content.Length);
        Assert.Equal(referenceHash, resultHash);
    }

    public void Dispose()
    {
        File.Delete("test_gpt_subject.vhd");
        File.Delete("ext4.vhd");
        File.Delete("ext4.loop");
        File.Delete("test_gpt.vhdx");
        File.Delete("test_gpt_back.vhd");
        File.Delete("test_gpt_from_qcow2.img");
    }
}