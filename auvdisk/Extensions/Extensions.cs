using DotNext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DiskAccessLibrary.VHD;

namespace auvdisk.Extensions
{
    internal static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TResult> ActWithLog<TResult>(this Func<TResult> function, Action<string> logger, string message, string errorLevel = "ERROR")
        {
            Result<TResult> result;
            try
            {
                logger(message);
                result = function();
            }
            catch (Exception e)
            {
                result = new(e);

                if (errorLevel != "")
                {
                    logger($"{errorLevel}: {e.Message}");
                }
            }

            return result;
        }
        
        public static string FormatDuPath(this string path, bool pretty = true)
        {
            if (pretty)
            {
                return path.TrimStart(new char[] { '\\', '/' }).Replace("\\", "/");
            }
            else
            {
                return path.TrimStart(new char[] { '\\', '/' }).Replace("/", "\\");
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

        public static Action WithCheckedVhdType(this Action action, string source, VirtualHardDiskType diskType, Action<string> logger)
        {
            return () =>
            {
                logger($"Checking that source VHD file is of type {diskType}");

                var vhdHeader = Vhd.Util.ReadVhdHeaderSafe(source);

                if (vhdHeader != null && vhdHeader.DiskType == diskType)
                {
                    action();
                }
                else if (vhdHeader != null)
                {
                    logger($"ERROR: expected VHD of type {diskType}, got {vhdHeader.DiskType}");
                }
                else
                {
                    logger($"ERROR: failed to read VHD header");
                }
            };
        }

        public static Action WithCheckedDiskType(this Action action, string diskType, string source, Action<string> logger, bool verbose)
        {
            return () =>
            {
                logger($"Checking that source file contains valid {diskType} image");

                var probeResult = new DiskProbe(source, 0, 0, (DiscFileSystem) => { }, verbose ? logger : (string s) => { }).Probe();

                if (probeResult.Disk == null)
                {
                    logger($"ERROR: no {diskType} footer and/or partition table found, exiting");
                }
                else if (probeResult.Disk.ImageType != diskType)
                {
                    logger($"ERROR: expected {diskType} image file got {probeResult.Disk.ImageType}, exiting");
                }
                else
                {
                    action();
                }
            };
        }

        public static Action WithCheckedFsType(this Action action, string fsType, string source, Action<string> logger, bool verbose)
        {
            return () =>
            {
                logger($"Checking that source file contains valid {fsType} image");

                var probeResult = new DiskProbe(source, 0, 0, (DiscFileSystem) => { }, verbose ? logger : (string s) => { }).Probe();

                if (probeResult.Fs == null)
                {
                    logger("ERROR: no filesystem found, exiting");
                }
                else if (probeResult.Fs.FsType != fsType && fsType != "")
                {
                    logger($"ERROR: expected {fsType} filesystem, got {probeResult.Fs.FsType}, exiting");
                }
                else
                {
                    action();
                }
            };
        }

        public static Action WithCheckedSourceExists(this Action action, string source, Action<string> logger)
        {
            return () =>
            {
                logger("Checking that source file exists");

                if (!File.Exists(source))
                {
                    logger($"ERROR: source file {source} does not exist");
                }
                else
                {
                    action();
                }
            };
        }

        public static Action WithCheckedTargetAvailable(this Action action, string target, Action<string> logger)
        {
            return () =>
            {
                logger("Checking that target file doesn't exists");

                if (File.Exists(target))
                {
                    logger($"ERROR: {target} already exists");
                }
                else
                {
                    action();
                }
            };
        }
    }
}
