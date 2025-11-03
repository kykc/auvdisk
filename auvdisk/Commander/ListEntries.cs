using System.Runtime.InteropServices;

namespace auvdisk.Commander
{
    interface IListEntry
    {
        string Name { get; }
        string FullPath { get; }
        string Caption { get; }
        bool IsDisk();
        bool IsDirectory();
        bool IsFile();
    }

    class DirEntry : IListEntry
    {
        public string FullPath { get; }
        private IFilesystem Fs { get; }
        public string Name => (IsDisk() || FullPath == "") ? FullPath : Fs.GetFileName(FullPath);
        public string Caption => $"[{Name}]";

        public DirEntry(string fullPath, IFilesystem fileSystem)
        {
            FullPath = fullPath;
            Fs = fileSystem;
        }

        public bool IsDisk()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && FullPath is [_, ':', _];
        }

        public bool IsDirectory()
        {
            return !IsDisk();
        }

        public bool IsFile()
        {
            return false;
        }

        public override string ToString()
        {
            return Caption;
        }
    }

    class FileEntry : IListEntry
    {
        public string FullPath { get; }
        public string Name => Fs.GetFileName(FullPath);
        public string Caption => Name;
        private IFilesystem Fs { get; }

        public FileEntry(string fullPath, IFilesystem fileSystem)
        {
            FullPath = fullPath;
            Fs = fileSystem;
        }

        public override string ToString()
        {
            return Caption;
        }

        public bool IsDisk()
        {
            return false;
        }

        public bool IsDirectory()
        {
            return false;
        }

        public bool IsFile()
        {
            return true;
        }
    }
}