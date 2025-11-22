#if WINDOWS

using System.Runtime.Versioning;
using auvdisk.Interop.Win32;
using DiscUtils.Ntfs;

namespace auvdisk.test.Interop.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public class BlockDeviceUnbufferedStreamTest
{
    [Theory]
    [InlineData(@"\\.\C:")]
    public void TestNtfsVolumeStream(string volumeId)
    {
        if (!Environment.IsPrivilegedProcess)
        {
            throw Xunit.Sdk.SkipException.ForSkip("This test requires administrator privileges on Windows platform");
        }

        using var stream = new BlockDeviceUnbufferedStream(volumeId);
        var fs = new NtfsFileSystem(stream);
        var clusterSize = fs.ClusterSize;
        var wmiVolumeCapacity = Util.GetVolumeCapacity(volumeId);
        Assert.NotNull(wmiVolumeCapacity);
        var expectedDiff = stream.Length % clusterSize > 0 ? stream.Length % clusterSize : clusterSize;
        Assert.Equal(expectedDiff, stream.Length - wmiVolumeCapacity.Value);

        Assert.Contains("Windows", fs.GetDirectories(@"\").Select(x => x.TrimStart(['\\', '/'])));
    }
}

#endif