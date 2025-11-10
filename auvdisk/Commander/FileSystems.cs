using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using auvdisk.Log;
using DiscUtils;
using DiscUtils.Wim;

namespace auvdisk.Commander
{
    interface IFilesystem
    {
        IEnumerable<string> GetFiles(string path);
        IEnumerable<string> GetDirectories(string path);
        Stream OpenFile(string path);
        string PathJoin(string p1, string p2);
        string GetFileName(string path);
        string GetDirectoryName(string path);
        FileInfo GetFileInfo(string path);

        IListEntry Cwd { get; set; }
    }

    record FileInfo(
        ulong Size,
        string Name,
        string FullPath,
        DateTime CreatedOnUtc,
        DateTime ModifiedOnUtc,
        FileAttributes Attributes);

    static class RealDiskFactory
    {
        public static IFilesystem MakeFs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsDisks();
            }
            else
            {
                return new PosixRoot();
            }
        }
    }

    class PosixRoot : IFilesystem
    {
        public PosixRoot()
        {
            Cwd = new DirEntry("/", this);
        }

        public IEnumerable<string> GetFiles(string path)
        {
            return Directory.GetFiles(path);
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            return Directory.GetDirectories(path);
        }

        public Stream OpenFile(string path)
        {
            return File.OpenRead(path);
        }

        public string PathJoin(string p1, string p2)
        {
            if (p1 == string.Empty || p1 == "/")
            {
                return $"/{p2}";
            }

            return $"{p1}/{p2}";
        }

        public string GetFileName(string path)
        {
            return path.Split('/').Last();
        }

        public string GetDirectoryName(string path)
        {
            var result = string.Join('/', path.Split('/').SkipLast(1));

            if (result == "")
            {
                return "/";
            }

            return result;
        }

        public FileInfo GetFileInfo(string path)
        {
            var createdOn = File.GetCreationTimeUtc(path);
            var modifiedOn = File.GetLastWriteTimeUtc(path);
            var length = (ulong)new System.IO.FileInfo(path).Length;
            var attrs = File.GetAttributes(path);

            return new FileInfo(length, GetFileName(path), path, createdOn, modifiedOn, attrs);
        }

        public IListEntry Cwd { get; set; }
    }

    class WindowsDisks : IFilesystem
    {
        private List<string> Letters { get; }

        public WindowsDisks()
        {
            var drives = DriveInfo.GetDrives();
            Letters = drives.Select(drive => drive.Name).ToList();
            Cwd = new DirEntry("", this);
        }

        public IEnumerable<string> GetFiles(string path)
        {
            if (path == "")
            {
                return new List<string>{ } ;
            }

            return Directory.GetFiles(path);
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            if (path == "")
            {
                return Letters;
            }

            return Directory.GetDirectories(path);
        }

        public Stream OpenFile(string path)
        {
            return File.OpenRead(path);
        }

        public string PathJoin(string p1, string p2)
        {
            if (p1 == string.Empty)
            {
                return p2;
            }
            else if (p1 is [_, ':', _])
            {
                return $"{p1}{p2}";
            }

            return $"{p1}\\{p2}";
        }

        public string GetFileName(string path)
        {
            return path.Split('\\').Last();
        }

        public string GetDirectoryName(string path)
        {
            if (path is [_, ':', _])
            {
                return "";
            }

            var result = string.Join('\\', path.Split('\\').SkipLast(1));

            if (result is [_, ':'])
            {
                return $"{result}\\";
            }

            return result;
        }

        public FileInfo GetFileInfo(string path)
        {
            var createdOn = File.GetCreationTimeUtc(path);
            var modifiedOn = File.GetLastWriteTimeUtc(path);
            var length = (ulong)new System.IO.FileInfo(path).Length;
            var attrs = File.GetAttributes(path);

            return new FileInfo(length, GetFileName(path), path, createdOn, modifiedOn, attrs);
        }

        public IListEntry Cwd { get; set; }
    }

    class DiscUtilsFs : IFilesystem
    {
        private static readonly char SepChar = Path.DirectorySeparatorChar;
        private static string Sep => SepChar.ToString();

        public Dictionary<string, DiscFileSystem> Fs { get; }

        public DiscUtilsFs(Dictionary<string, DiscFileSystem> fs)
        {
            Fs = fs;
            Cwd = new DirEntry(Sep, this);
        }

        public IEnumerable<string> GetFiles(string path)
        {
            if (path == Sep)
            {
                return new List<string>();
            }

            var tokens = path.TrimStart(SepChar).Split(SepChar);

            return Fs[tokens.First()]
                .GetFiles(string.Join(SepChar, tokens.Skip(1)))
                .Select(x => PathJoin(tokens.First(), x));
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            if (path == Sep)
            {
                return Fs.Keys.ToList();
            }

            var tokens = path.TrimStart(SepChar).Split(SepChar);

            return Fs[tokens.First()]
                .GetDirectories(string.Join(SepChar, tokens.Skip(1)))
                .Select(x => PathJoin(tokens.First(), x));
        }

        public Stream OpenFile(string path)
        {
            var tokens = path.TrimStart(SepChar).Split(SepChar);
            var volume = tokens.First();
            var filePath = string.Join(SepChar, tokens.Skip(1));

            return Fs[volume].OpenFile(filePath, FileMode.Open, FileAccess.Read);
        }

        public string PathJoin(string p1, string p2)
        {
            if (p1 == String.Empty || p1 == Sep)
            {
                return $"{Sep}{p2}";
            }

            return $"{p1}{Sep}{p2}";
        }

        public string GetFileName(string path)
        {
            return path.Split(SepChar).Last();
        }

        public string GetDirectoryName(string path)
        {
            var result = string.Join(SepChar, path.Split(SepChar).SkipLast(1));

            if (result == String.Empty)
            {
                return Sep;
            }

            return result;
        }

        public FileInfo GetFileInfo(string path)
        {
            var tokens = path.TrimStart(SepChar).Split(SepChar);
            var volume = tokens.First();
            var filePath = string.Join(SepChar, tokens.Skip(1));

            var createdOn = Fs[volume].GetCreationTimeUtc(filePath);
            var modifiedOn = Fs[volume].GetLastWriteTimeUtc(filePath);
            var length = (ulong)Fs[volume].GetFileInfo(filePath).Length;
            var attrs = Fs[volume].GetAttributes(filePath);

            return new FileInfo(length, GetFileName(path), path, createdOn, modifiedOn, attrs);
        }

        public IListEntry Cwd { get; set; }
    }
}