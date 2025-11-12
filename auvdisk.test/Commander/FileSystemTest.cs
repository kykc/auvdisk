using System.Runtime.InteropServices;
using auvdisk.DiskImage;
using DiscUtils;

namespace auvdisk.test.Commander
{
    public class FileSystemTest
    {
        public FileSystemTest()
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();
            Program.IsInteractive = false;
        }

        [Fact]
        public void TestNativeFsPathManipulation()
        {
            var fs = auvdisk.Commander.RealDiskFactory.MakeFs();
            var currentDir = Directory.GetCurrentDirectory();
            var testData = "testdata";
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            Assert.Equal(Path.Join(currentDir, testData), fs.PathJoin(currentDir, testData));
            Assert.Equal(currentDir, fs.GetDirectoryName(fs.PathJoin(currentDir, testData)));
            Assert.Equal(testData, fs.GetFileName(fs.PathJoin(currentDir, testData)));


            // This looks a bit strange, but it allows to use the same code for Windows/Posix paths testing
            // Would be cool to be able to run them both on any platform...
            var currentRoot = isWindows ? (currentDir.First() + @":\") : "/";
            var topSubdir = Path.GetFileName(Directory.GetDirectories(currentRoot).First());

            Assert.Equal(Path.Join(currentRoot, topSubdir), fs.PathJoin(currentRoot, topSubdir));

            Assert.Equal(currentRoot, fs.GetDirectoryName(fs.PathJoin(currentRoot, topSubdir)));
            Assert.Equal(topSubdir, fs.GetFileName(fs.PathJoin(currentRoot, topSubdir)));

            Assert.Equal(isWindows ? "" : "/", fs.GetDirectoryName(currentRoot));
            Assert.Equal("", fs.GetFileName(currentRoot));

            Assert.Equal(isWindows ? "" : "/", fs.GetDirectoryName(""));
            Assert.Equal("", fs.GetFileName(""));
        }

        [Fact]
        public void TestNativeFsGetDirectoriesFiles()
        {
            var fs = auvdisk.Commander.RealDiskFactory.MakeFs();
            var currentDir = Directory.GetCurrentDirectory();
            var testData = "testdata";
            var bcdFilename = "BCD";
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            Assert.Contains(testData, fs.GetDirectories(currentDir).Select(x => fs.GetFileName(x)));
            Assert.Contains(bcdFilename, fs.GetFiles(fs.PathJoin(currentDir, testData)).Select(x => fs.GetFileName(x)));

            var currentRoot = isWindows ? (currentDir.First() + @":\") : "/";
            var topSubdir = Path.GetFileName(Directory.GetDirectories(currentRoot).First());

            Assert.Contains(topSubdir, fs.GetDirectories(currentRoot).Select(x => fs.GetFileName(x)));
        }

        [Fact]
        public void TestDiscUtilsFs()
        {
            using var stream = File.OpenRead(Path.Join(Directory.GetCurrentDirectory(), "testdata", "ext4.loop"));
            var fsInfos = FileSystemManager.DetectFileSystems(stream).Select(x => x.Open(stream)).ToList();
            var sep = Path.DirectorySeparatorChar.ToString();

            Assert.Single(fsInfos);

            var fs = new auvdisk.Commander.DiscUtilsFs(Factory.GetFileSystems(fsInfos));
            var ext4Caption = fs.GetDirectories(sep).Select(x => fs.GetFileName(x)).First();
            Assert.Equal(fs.Fs.Keys.First(), ext4Caption);
            var ext4RootFiles = fs.GetFiles($"{sep}{ext4Caption}").ToList();

            Assert.Contains("test_root.txt", ext4RootFiles.Select(x => fs.GetFileName(x)));
            using var file = fs.OpenFile(ext4RootFiles.First());
            var reader = new StreamReader(file);
            Assert.Equal("test_root", reader.ReadToEnd().TrimEnd('\n', '\r'));
        }
    }
}