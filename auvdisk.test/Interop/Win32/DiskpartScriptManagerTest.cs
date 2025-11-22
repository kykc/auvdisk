#if WINDOWS
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace auvdisk.test.Interop.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public partial class DiskpartScriptManagerTest
{
    [Fact]
    public void TestListDisk()
    {
        if (!Environment.IsPrivilegedProcess)
        {
            throw Xunit.Sdk.SkipException.ForSkip("This test requires administrator privileges on Windows platform");
        }
        
        var logger = new LogWatcher();
        var result = auvdisk.Interop.Win32.DiskpartScriptMananger.Execute("\"list disk\" | diskpart", logger);
        Assert.False(result.IsErr);
        var output = result.UnwrapVal();
        Assert.Equal(0, output.ExitCode);
        var stdOut = output.StandardOutput.Trim();
        var lines = ToLines(stdOut).ToList();
        
        Assert.Contains(lines, x => x.StartsWith("Microsoft DiskPart version"));
        Assert.Contains(lines, x => DiskpartOnlineDiskRegex().IsMatch(x));
    }

    private static IEnumerable<string> ToLines(string text)
    {
        using var sr = new StringReader(text);
        while (sr.ReadLine() is { } line) {
            yield return line;
        }
    }

    [GeneratedRegex(@"^\s+Disk\s\d+\s+Online")]
    private static partial Regex DiskpartOnlineDiskRegex();
}
#endif