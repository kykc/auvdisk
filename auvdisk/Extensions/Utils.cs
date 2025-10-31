using System.Numerics;
using auvdisk.DiskImage;
using auvdisk.Log;
using CommandLine;
using DiskAccessLibrary.VHD;

namespace auvdisk.Extensions
{
    internal static class Utils
    {
        public static TResult? SuppressRef<TException, TResult>(Func<TResult> func, Log.ILog? logger = null)
            where TException : Exception
            where TResult : class
        {
            try
            {
                return func();
            }
            catch (TException e)
            {
                logger?.Warning(e.Message);

                return null;
            }
        }

        public static TResult? SuppressVal<TException, TResult>(Func<TResult> func, Log.ILog? logger = null)
            where TException : Exception
            where TResult : struct
        {
            try
            {
                return func();
            }
            catch (TException e)
            {
                logger?.Warning(e.Message);

                return null;
            }
        }

        public static string ToReadableString(this DiscUtils.Partitions.PartitionTable partitionTable)
        {
            if (partitionTable is DiscUtils.Partitions.GuidPartitionTable)
            {
                return "GPT";
            }
            else if (partitionTable is DiscUtils.Partitions.BiosPartitionTable)
            {
                return "MBR";
            }
            else
            {
                return partitionTable.GetType().ToString();
            }
        }

        public static bool If(Action action, Func<bool> condition)
        {
            if (condition())
            {
                action();

                return true;
            }

            return false;
        }

        public static Flow<DiskProbe.ProbeResult> WithCheckedVhdType<TSubj>(this Flow<TSubj> action, string source, VirtualHardDiskType diskType)
            where TSubj : class
        {
            return action.Log($"Checking that source VHD file is of type {diskType}")
                .MapOr((_) => DiskImage.Vhd.Util.ReadVhdFooterSafe(source), "Failed to read Vhd footer")
                .Check((footer) => footer.IsValid, (_) => "Invalid VHD footer format")
                .Check((footer) => footer.DiskType == diskType, (footer) => $"Expected VHD of type {diskType}, got {footer.DiskType}")
                .Map((footer) => new DiskProbe(source, new NullLogger()).Probe());
        }

        public static Flow<DiskProbe.ProbeResult> WithCheckedDiskType<TSubj>(this Flow<TSubj> action, string diskType, string source, bool verbose)
            where TSubj : class
        {
            var probeLogger = verbose ? action.Logger : new Log.NullLogger();

            return action.Log($"Checking that source file contains valid {(diskType == "" ? "disk" : diskType)} image")
                .Map((_) => new DiskProbe(source, probeLogger, fs => { }).Probe())
                .Check((res) => res.Disk != null, (res) => $"No {diskType} footer and/or partition table found, exiting")
                .Check((res) => res.Disk!.ImageType == diskType || diskType == "", (res) => $"Expected {diskType} image file got {res.Disk!.ImageType}, exiting");
        }

        public static Flow<DiskProbe.ProbeResult> WithCheckedFsType<TSubj>(this Flow<TSubj> action, string fsType, string source, bool verbose)
            where TSubj : class
        {
            var probeLogger = verbose ? action.Logger : new Log.NullLogger();

            return action.Log($"Checking that source file contains valid {(fsType == "" ? "filesystem" : fsType)} image")
                .Map((_) => new DiskProbe(source, probeLogger, fs => { }).Probe())
                .Check((res) => res.Fs != null, (_) => "No filesystem found, exiting")
                .Check((res) => res.Fs!.FsType == fsType || fsType == "", (res) => $"Expected {fsType} filesystem, got {res.Fs!.FsType}, exiting");
        }

        public static Flow<TSubj> WithCheckedSourceExists<TSubj>(this Flow<TSubj> action, string source)
            where TSubj : class
        {
            return action.Log("Checking that source file exists")
                .Check((_) => File.Exists(source), (_) => $"Source file {source} does not exist");
        }

        public static Flow<Value<long>> WithCheckedStreamBoundaries<TSubj>(this Flow<TSubj> action, string path, ulong offset, ulong length)
            where TSubj : class
        {
            return action.Log("Checking file stream boundaries")
                .Map((_) => new FileStream(path, FileMode.Open, FileAccess.Read))
                .MapDispose((fs) => fs.Length.Some())
                .Check((streamLength) => (ulong)streamLength.Val < offset + length, (_) => "Requested operation exceeds file length");
        }

        public static Flow<TSubj> WithCheckedTargetAvailable<TSubj>(this Flow<TSubj> action, string target) where TSubj : class
        {
            return action.Log("Checking that target file doesn't exists")
                .Check((_) => !File.Exists(target), (_) => $"{target} already exists");
        }

        public static Value<T> Some<T>(this T value) where T: struct => new(value);
    }
}
