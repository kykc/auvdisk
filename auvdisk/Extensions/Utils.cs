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
    public interface IProgressData
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

        public static bool IfElse(Func<bool> condition, Action actionTrue, Action actionFalse)
        {
            if (condition())
            {
                actionTrue();
                
                return true;
            }
            else
            {
                actionFalse();
                return false;
            }
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
        
        public static Flow<TSubj> WithCheckedVhdType<TSubj>(this Flow<TSubj> action, Func<TSubj, string> source, Func<TSubj, VirtualHardDiskType> diskType, ILog logger)
            where TSubj : class
        {
            return action.LogOk(logger, x => $"Checking that source VHD file is of type {diskType(x)}")
                .MapOr(x => new { footer = DiskImage.Vhd.Util.ReadVhdFooterSafe(source(x)), opts = x }, "Failed to read Vhd footer")
                .Check(x => x.footer!.IsValid, (_) => "Invalid VHD footer format")
                .Check(x => x.footer!.DiskType == diskType(x.opts), x => $"Expected VHD of type {diskType(x.opts)}, got {x.footer!.DiskType}")
                .Map(x => x.opts);
        }
        
        public static Flow<TSubj> WithCheckedDiskType<TSubj>(this Flow<TSubj> action, Func<TSubj, string> diskType, Func<TSubj, string> source, Func<TSubj, bool> verbose, ILog logger)
            where TSubj : class
        {
            return action
                .LogOk(logger, x => $"Checking that source file contains valid {(diskType(x) == "" ? "disk" : diskType(x))} image")
                .Map(x => new {opts = x, probe = new DiskProbe(source(x), verbose(x) ? logger : new NullLogger(), fs => { }).Probe()})
                .Check((res) => res.probe.Disk != null, x => $"No {diskType(x.opts)} footer and/or partition table found, exiting")
                .Check((res) => res.probe.Disk!.ImageType == diskType(res.opts) || diskType(res.opts) == "", (res) => $"Expected {diskType(res.opts)} image file got {res.probe.Disk!.ImageType}")
                .Map(x => x.opts);
        }
        
        public static Flow<TSubj> WithCheckedFsType<TSubj>(this Flow<TSubj> action, Func<TSubj, string> fsType, Func<TSubj, string> source, Func<TSubj, bool> verbose, ILog logger)
            where TSubj : class
        {
            return action
                .LogOk(logger, x => $"Checking that source file contains valid {(fsType(x) == "" ? "filesystem" : fsType(x))} image")
                .Map(x => new { opts = x, probe = new DiskProbe(source(x), verbose(x) ? logger : new NullLogger() , fs => { }).Probe() })
                .Check(x => x.probe.Fs != null, (_) => "No filesystem found, exiting")
                .Check(x => x.probe.Fs!.FsType == fsType(x.opts) || fsType(x.opts) == "", x => $"Expected {fsType(x.opts)} filesystem, got {x.probe.Fs!.FsType}")
                .Map(x => x.opts);
        }
        
        public static Flow<TSubj> WithCheckedSourceExists<TSubj>(this Flow<TSubj> action, Func<TSubj, string> source, ILog logger)
            where TSubj : class
        {
            TSubj TryOpenForReading(TSubj subj)
            {
                File.OpenRead(source(subj))?.Close();
                return subj;
            }

            return action.LogOk(logger, "Checking that source file exists")
                .Check(x => File.Exists(source(x)), (x) => $"Source file {source(x)} does not exist")
                .TryMap(TryOpenForReading, (Exception e) => e.Message);
        }
        
        public static Flow<TSubj> WithCheckedStreamBoundaries<TSubj>(this Flow<TSubj> action, Func<TSubj, string> path, Func<TSubj, ulong> offset, Func<TSubj, ulong> length, ILog logger)
            where TSubj : class
        {
            return action.LogOk(logger, "Checking file stream boundaries")
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
        
        public static Flow<TSubj> WithCheckedPartLayout<TSubj>(this Flow<TSubj> action, Func<TSubj, string> layout, ILog logger) where TSubj : class
        {
            return action.LogIf(logger, x => layout(x) != "", "Parsing partition layout string")
                .CheckDiscardIf(x => layout(x) != "", x => PartitionTable.Util.ParseLayout(layout(x), logger));
        }
        
        public static Flow<TSubj> WithCheckedTargetAvailable<TSubj>(this Flow<TSubj> action, Func<TSubj, string> target, ILog logger) where TSubj : class
        {
            TSubj TryCreate(TSubj subj)
            {
                new FileStream(target(subj), FileMode.CreateNew, FileAccess.ReadWrite).Close();
                File.Delete(target(subj));

                return subj;
            }
            
            return action.LogOk(logger, "Checking that target file doesn't exist")
                .Check(subj => !Path.Exists(target(subj)), (subj) => $"{target(subj)} already exists")
                .TryMap(TryCreate, (Exception e) => e.Message);
        }

        // Not because I have nothing better to do, but because DiscUtils is picky and is using file extension to guess file type
        public static Flow<TSubj> WithCheckedTargetExtension<TSubj>(this Flow<TSubj> action, Func<TSubj, string> target,
            Func<TSubj, string> targetExt) where TSubj : class
        {
            bool CheckExtension(TSubj subj)
            {
                var targetValue = target(subj);
                var correctExt = targetExt(subj);
                var extension = Path.GetExtension(targetValue);

                return string.Equals(extension, correctExt, StringComparison.InvariantCultureIgnoreCase);
            }

            return action
                .Check(CheckExtension, _ => "Target extension is invalid for selected virtual disk type");
        }

        public static Flow<TSubj> WithCheckedSize<TSubj>(this Flow<TSubj> action, Func<TSubj, string> size) where TSubj : class
        {
            return action.Check((subj) => size(subj).ParseByteLength().HasValue, (_) => "Failed to parse size in bytes");
        }

        public static Value<T> RefVal<T>(this T value) where T: struct => new(value);

        public static ulong DivideAndCeil(this ulong value, ulong divisor)
        {
            return (ulong)Math.Ceiling((double)value / (double)divisor);
        }
    }
}
