#if WINDOWS
using auvdisk.Interop;
using Win32.TokenPrivileges;
#endif
using System.Diagnostics;
using System.Runtime.InteropServices;
using auvdisk.Bytes;
using auvdisk.Cli;
using auvdisk.DiskImage.Vhd;
using auvdisk.Extensions;
using auvdisk.Log;
using Spectre.Console;

namespace auvdisk.Fs
{
    public static class Util
    {
#if WINDOWS
        private static readonly object Lock = new();
        private static object? Privilege = null;
#endif
        
#pragma warning disable CA1416
        private static bool ResizeFileFastUnsafe(string target, ulong size, Log.ILog logger)
        {
#if WINDOWS
            return auvdisk.Interop.Win32.Util.ResizeFileFastUnsafe(target, size, logger).IsSuccess;
#else
            logger.Error("This operation is not supported on this platform");
            return false;
#endif
        }

        public static bool? IsSparseFile(string target, Log.ILog logger)
        {
#if WINDOWS
            return auvdisk.Interop.Win32.Util.IsSparseFile(target, logger);
#else
            logger.Error("This operation is not supported on this platform");
            return null;
#endif
        }

#pragma warning restore CA1416
        
        private static bool ResizeFile(string target, ulong size, Log.ILog logger)
        {
            using var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None).WithProgress();
            var currentSize = stream.Length;
            System.Diagnostics.Debug.Assert(size > (ulong)currentSize);
            var difference = size - (ulong)currentSize;
            var bufferSize = StreamCopyProgressWrapper.DefaultCopyBufferSize;
            var remainder = difference % (ulong)bufferSize;
            
            stream.Seek(0, SeekOrigin.End);
            stream.ZeroFill(difference / (ulong)bufferSize, bufferSize, logger);
            
            var remainderBytes = Enumerable.Repeat((byte)0x0, (int)remainder).ToArray();
            stream.Write(remainderBytes, 0, remainderBytes.Length);
            
            return true;
        }

        public static string? ExtractUuid(DiscUtils.DiscFileSystem fs, Log.ILog logger)
        {
            if (fs is DiscUtils.Ntfs.NtfsFileSystem)
            {
                return Ntfs.UuidExtractor.ExtractUuid(fs, logger);
            }
            else if (fs is DiscUtils.Fat.FatFileSystem)
            {
                return Fat.UuidExtractor.ExtractUuid(fs, logger);
            }
            else if (fs is DiscUtils.Ext.ExtFileSystem)
            {
                return Ext.UuidExtractor.ExtractUuid(fs, logger).ToString();
            }
            else
            {
                return null;
            }
        }

        public static string? GetUuid(this DiscUtils.DiscFileSystem fs, Log.ILog logger)
        {
            return ExtractUuid(fs, logger);
        }
        
        public static void ExtractFileSegment(string source, string target, ulong offset, ulong length, ILog logger)
        {
            using var rawFileStream = new System.IO.FileStream(source, FileMode.Open, FileAccess.Read);
            using var decoratedStream = new SegmentStream(rawFileStream, (long)offset, (long)length);
            using var targetStream = new System.IO.FileStream(target, FileMode.CreateNew, FileAccess.Write);
            decoratedStream.WithProgress().CopyTo(targetStream, logger);
        }
        
        public static bool HandleResizeFile(string target, ulong size, bool forceZeroFill, Log.ILog logger)
        {
            bool success = false;

            if (!File.Exists(target))
            {
                logger.Error($"File {target} does not exist");
                return false;
            }
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !forceZeroFill)
            {
                var prompt = () => AnsiConsole.Confirm("Zero-fill was not requested. Proceed with fast/unsafe method? (You may receive an UAC prompt)");
                
                if (!Environment.IsPrivilegedProcess && Program.IsInteractive && prompt())
                {
                    try
                    {
                        string[] args = ["resize-file-unsafe", "--target", target, "--size", size.ToString()];
                        string self = Process.GetCurrentProcess().MainModule!.FileName;

                        int exitCode = 128;

                        Cli.SimpleExec.Command
                            .RunAsync(self, args, handleExitCode: HandleExitCode, asAdmin: true, noEcho: true)
                            .GetAwaiter().GetResult();

                        bool HandleExitCode(int arg)
                        {
                            exitCode = arg;
                            return true;
                        }

                        success = exitCode == 0;

                        if (success)
                        {
                            logger.Log("Resized file with fast/unsafe method");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"Failed to start elevated process with error {ex.Message}");
                    }
                }
#if WINDOWS
                if (Environment.IsPrivilegedProcess && !success)
                {
                    try
                    {
                        logger.Log("Resizing file with  fast/unsafe method");
                        logger.Log($"Administrator privileges: {Environment.IsPrivilegedProcess}");
                        var currentProcess = Process.GetCurrentProcess();

                        // Creates a lot of weirdness when many threads run this from UTs.
                        // This leads to token being held until process finishes "in production"
                        // However, auvdisk isn't designed to be hanging around, so this shouldn't be
                        // a real issue.
                        lock (Lock)
                        {
                            Privilege ??= new AdjustPrivilege(PrivilegeName.SeManageVolumePrivilege);

                            if (Program.IsInteractive)
                            {
                                var canManagerVolume = PrivilegeProvider.HasPrivilege(null, currentProcess, PrivilegeName.SeManageVolumePrivilege);

                                logger.Log($"SeManageVolumePrivilege: {canManagerVolume}");
                            }

                            success = ResizeFileFastUnsafe(target, size, logger);

                            if (success)
                            {
                                logger.Log("Resized file with fast/unsafe method");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(Spectre.Console.Markup.Escape(ex.Message));
                    }
                }
#endif
                if (!success)
                {
                    logger.Log("Falling back to slow mode");
                    success = ResizeFile(target, size, logger);
                }
            }
            else if (CliTools.IsDdPresent() && !forceZeroFill)
            {
                logger.Log("Found dd, will try to speed things up");
                logger.Warning("This may result in a sparse file, if target file is on NTFS or SMB share");

                var ddResult = CliTools.AllocateWithDd(target, size, logger);

                success = !ddResult.IsError() || ResizeFile(target, size, logger);
            }
            else
            {
                success = ResizeFile(target, size, logger);
            }

            return success;
        }
    }
}
