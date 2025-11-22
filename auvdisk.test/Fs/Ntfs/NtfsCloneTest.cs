using auvdisk.Fs.Ntfs;
using DiscUtils.Ntfs;
using DiscUtils.Streams;

namespace auvdisk.test.Fs.Ntfs;

public class NtfsCloneTest
{
    [Theory]
    [MemberData(nameof(TestNtfsCloneFromVhdData))]
    public void TestNtfsCloneFromVhd(string pathToVhd, int partitionIdx, string controlFilePath, string controlFileContent)
    {
        var logger = new LogWatcher();

        using var vhdResult = DiskImage.Vhd.Util.OpenDiskWithDu(pathToVhd, logger);
        Assert.False(vhdResult.IsErr);
        var vhd = vhdResult.UnwrapVal();
        Assert.True(vhd.IsPartitioned);
        Assert.True(vhd.Partitions.Count > partitionIdx);

        using var target = new MemoryStream();
        
        var result = NtfsClone.Clone(vhd.Partitions[partitionIdx].Open(), target, logger);
        
        Assert.False(result.IsErr);

        using var sourceNtfs = new NtfsFileSystem(vhd.Partitions[partitionIdx].Open());
        using var clonedNtfs = new NtfsFileSystem(target);
        
        Assert.Equal(sourceNtfs.SectorSize, clonedNtfs.SectorSize);
        Assert.Equal(sourceNtfs.ClusterSize, clonedNtfs.ClusterSize);
        Assert.Equal(sourceNtfs.VolumeLabel, clonedNtfs.VolumeLabel);
        Assert.Equal(sourceNtfs.VolumeId, clonedNtfs.VolumeId);
        Assert.Equal(sourceNtfs.Size, clonedNtfs.Size);
        
        Assert.True(clonedNtfs.FileExists(controlFilePath));
        Assert.Equal(controlFileContent, ReadFileContents(clonedNtfs.OpenFile(controlFilePath, FileMode.Open, FileAccess.Read)));
    }

    private static string ReadFileContents(Stream source)
    {
        var reader = new StreamReader(source);
        return reader.ReadToEnd();
    }
    
    public static TheoryData<string, int, string, string> TestNtfsCloneFromVhdData =>
        new()
        {
            { Path.Join("testdata", "test_gpt.vhd"), 0, "/test_text.txt", "test_text" },
            { Path.Join("testdata", "test_gpt_child.vhd"), 0, "/child_text.txt", "child_text" },
        };
}
