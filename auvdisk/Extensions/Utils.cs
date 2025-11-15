using System.Numerics;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using auvdisk.Bytes;
using auvdisk.DiskImage;
using auvdisk.Interop;
using auvdisk.Log;
using CommandLine;
using DiskAccessLibrary.VHD;
using Spectre.Console;

namespace auvdisk.Extensions
{
    interface IProgressData
    {
        string Description { get; }
        string Complete { get; }
        long TotalBytes { get; }
        int IncrementBytes { get; set; }
    }
    
    internal static class Utils
    {
        public static void WithProgress(ILog logger, IProgressData startData, Func<Throttle<IProgressData>?, IProgressData> action,
            bool? forceProgress = null)
        {
            if (!Program.IsInteractive || (forceProgress ?? false))
            {
                action(null);
                return;
            }
            
            AnsiConsole.Progress()
                .Columns(new ElapsedTimeColumn(), new ProgressBarColumn(), new PercentageColumn(), new TaskDescriptionColumn(), new RemainingTimeColumn(), new TransferSpeedColumn(), new DownloadedColumn())
                .AutoRefresh(false)
                .AutoClear(false)
                .HideCompleted(false)
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"[green]{startData.Description.EscapeMarkup()}[/]", maxValue: startData.TotalBytes);
                    
                    var throttle = new Throttle<IProgressData>(Update, Program.ProgressReportRate);
                    
                    var result = action(throttle);
                    Update(result);
                    return;

                    void Update(IProgressData data)
                    {
                        task.Description = $"[green]{data.Description.EscapeMarkup()}[/]";
                        task.Increment(data.IncrementBytes);
                        data.IncrementBytes = 0;
                        ctx.Refresh();
                    }
                });
        
            logger.Log($"[green]{startData.Complete.EscapeMarkup()}[/]");
        }
        
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

        public static StreamCopyProgressWrapper WithProgress<TStream>(this TStream subject) where TStream : Stream
        {
            return new StreamCopyProgressWrapper(subject);
        }

        public static string HumanizeFilesize(ulong size, bool human = true)
        {
            if (!human)
            {
                return size.ToString();
            }

            var units = new List<string>(){
                "B", "KiB", "MiB", "GiB", "TiB"
            };

            ulong bytes = size;

            double pow = Math.Floor((bytes>0 ? Math.Log(bytes) : 0) / Math.Log(1024));
            pow = Math.Min(pow, units.Count-1);
            double value = (double)bytes / Math.Pow(1024, pow);
            return value.ToString(pow==0 ? "F0" : "F2") + "" + units[(int)pow];
        }

        public static string HumanizeBytes(this ulong bytes, bool human = true)
        {
            return HumanizeFilesize(bytes, human);
        }
        
        public static string HumanizeBytes(this long bytes, bool human = true)
        {
            return HumanizeFilesize((ulong)bytes, human);
        }

        public static ulong? ParseByteLength(this string str)
        {
            var units = new List<string>(){
                "B", "KiB", "MiB", "GiB", "TiB"
            }.Select(x => x.ToLower()).ToList();

            str = str.Replace(" ", "");
            str = str.Replace(",", "");
            str = str.ToLower();

            Regex regex = new Regex(@"^([\d\.]+)(.*)");

            var match = regex.Match(str);

            if (match.Success)
            {
                var amountString = match.Groups[1].Value;
                var unit = match.Groups[2].Value;
                var amount = SuppressVal<FormatException, decimal>(() => Convert.ToDecimal(amountString));

                if (amount.HasValue && unit == "")
                {
                    return (ulong)Math.Ceiling(amount.Value);
                }
                else if (units.Contains(unit) && amount.HasValue)
                {
                    ulong multiplier = 1;
                    int idx = units.FindIndex(x => x == unit);

                    for (int i = 0; i < idx; ++i)
                    {
                        multiplier *= 1024;
                    }

                    return (ulong)Math.Ceiling(amount.Value * multiplier);
                }
            }

            return null;
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

        public static Table MakeConsoleTable(string[] columns)
        {
            var table = new Table();

            foreach (var column in columns)
            {
                table.AddColumn(column);
            }

            return table;
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
            return action.WithCheckedSourceExists((_) => source);
        }
        
        public static Flow<TSubj> WithCheckedSourceExists<TSubj>(this Flow<TSubj> action, Func<TSubj, string> source)
            where TSubj : class
        {
            TSubj TryOpenForReading(TSubj subj)
            {
                File.OpenRead(source(subj))?.Close();
                return subj;
            }

            return action.Log("Checking that source file exists")
                .Check(x => File.Exists(source(x)), (x) => $"Source file {source(x)} does not exist")
                .TryMap<TSubj, Exception>(TryOpenForReading);
        }
        
        public static Flow<TSubj> WithCheckedStreamBoundaries<TSubj>(this Flow<TSubj> action, Func<TSubj, string> path, Func<TSubj, ulong> offset, Func<TSubj, ulong> length)
            where TSubj : class
        {
            return action.Log("Checking file stream boundaries")
                .Map(x => new {opts = x, fs = new FileStream(path(x), FileMode.Open, FileAccess.Read)})
                .Map(x =>
                {
                    var len = x.fs.Length;
                    x.fs.Dispose();
                    return new { x.opts, length = len };
                })
                .Check(x => (ulong)x.length < offset(x.opts) + length(x.opts), (_) => "Requested operation exceeds file length")
                .Map(x => x.opts);
        }

        public static Flow<TSubj> WithCheckedTargetAvailable<TSubj>(this Flow<TSubj> action, string target) where TSubj : class
        {
            return action.WithCheckedTargetAvailable((_) => target);
        }
        
        public static Flow<TSubj> WithCheckedTargetAvailable<TSubj>(this Flow<TSubj> action, Func<TSubj, string> target) where TSubj : class
        {
            TSubj TryCreate(TSubj subj)
            {
                new FileStream(target(subj), FileMode.CreateNew, FileAccess.ReadWrite).Close();
                File.Delete(target(subj));

                return subj;
            }
            
            return action.Log("Checking that target file doesn't exist")
                .Check(subj => !Path.Exists(target(subj)), (subj) => $"{target(subj)} already exists")
                .TryMap<TSubj, Exception>(TryCreate);
        }

        public static Flow<TSubj> WithCheckedSize<TSubj>(this Flow<TSubj> action, string size) where TSubj : class
        {
            return action.WithCheckedSize((_) => size);
        }
        
        public static Flow<TSubj> WithCheckedSize<TSubj>(this Flow<TSubj> action, Func<TSubj, string> size) where TSubj : class
        {
            return action.Check((subj) => size(subj).ParseByteLength().HasValue, (_) => "Failed to parse size in bytes");
        }

        public static Value<T> Some<T>(this T value) where T: struct => new(value);

        public static ulong DivideAndCeil(this ulong value, ulong divisor)
        {
            return (ulong)Math.Ceiling((double)value / (double)divisor);
        }
    }
}
