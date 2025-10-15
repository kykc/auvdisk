using DotNext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk.Extensions
{
    internal static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TResult> ActWithLog<TResult>(this Func<TResult> function, Action<string> logger, string message)
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
                logger("Error: " + e.Message);
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

        public static Action WithCheckedDiskType(this Action action, string diskType, string source, Action<string> logger)
        {
            return () =>
            {
                logger($"Checking that target file contains valid {diskType} image");

                var probeResult = new DiskProbe(source, 0, 0, (DiscFileSystem) => { }, (string s) => { }).Probe();

                if (probeResult.Disk == null)
                {
                    logger($"Error: no {diskType} footer and/or partition table found, exiting");
                }
                else if (probeResult.Disk.ImageType != diskType)
                {
                    logger($"Error: expected {diskType} image file got {probeResult.Disk.ImageType}, exiting");
                }
                else
                {
                    action();
                }
            };
        }

        public static Action WithCheckedFsType(this Action action, string fsType, string source, Action<string> logger)
        {
            return () =>
            {
                logger($"Checking that target file contains valid {fsType} image");

                var probeResult = new DiskProbe(source, 0, 0, (DiscFileSystem) => { }, (string s) => { }).Probe();

                if (probeResult.Fs == null)
                {
                    logger("Error: no filesystem found, exiting");
                }
                else if (probeResult.Fs.FsType != fsType)
                {
                    logger($"Error: expected {fsType} filesystem, got {probeResult.Fs.FsType}, exiting");
                }
                else
                {
                    action();
                }
            };
        }
    }
}
