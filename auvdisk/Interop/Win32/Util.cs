#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using auvdisk.Cli;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils.Fat;
using DotNext.Collections.Generic;
using System.Management;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Wdk.Storage.FileSystem;
using Windows.Wdk.System.SystemServices;
using Windows.Win32.System.IO;
using auvdisk.Fs.Ntfs;
using DiscUtils;
using Spectre.Console;

namespace auvdisk.Interop.Win32
{
    [SupportedOSPlatform("windows5.1.2600")]
    [ExcludeFromCodeCoverage]
    internal static class Util
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public const uint GENERIC_WRITE = 0x40000000;
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public const uint GENERIC_READ = 0x80000000;
        
        [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
        public record CreateFileFastUnsafeResult(bool HandleCreated, bool SetFilePointerResult, bool SetEndOfFileResult,
            string SetFileAllocationInfoResult, string SetFileDataLengthResult, bool CloseResult, bool IsSuccess);

        [SupportedOSPlatform("windows5.1.2600")]
        public static CreateFileFastUnsafeResult ResizeFileFastUnsafe(string target, ulong size, Log.ILog logger)
        {
            unsafe
            {
                FILE_VALID_DATA_LENGTH_INFORMATION dlInfo = new FILE_VALID_DATA_LENGTH_INFORMATION
                {
                    ValidDataLength = (long)size
                };

                FILE_ALLOCATION_INFO allocInfo = new FILE_ALLOCATION_INFO
                {
                    AllocationSize = (long)size
                };

                /*
                 * I lost a whole day here. Tests were randomly failing with file handles disappearing (getting invalid) in the middle of
                 * the write operations, etc. "Binary search" debugging led me here. I cannot fathom what actually happens and why CreateFile
                 * PInvoke seems to break things, but obtaining SafeFileHandle from FileStream doesn't seem to cause those issues.
                 * I'll leave it be. It's even a bit nicer solution (less PInvoke is always welcome), so no harm done. Probably.
                 * UPDATE: there was a bug which lead to passing a non-existing path here, and with OPEN_ALWAYS this might have
                 * contributed to the observed behavior. 🙈 <= this is me right now.
                 */
                /*var createResult = PInvoke.CreateFile(
                    target,
                    GENERIC_WRITE,
                    FILE_SHARE_MODE.FILE_SHARE_NONE,
                    lpSecurityAttributes: null,
                    FILE_CREATION_DISPOSITION.OPEN_ALWAYS,
                    0,
                    hTemplateFile: null);*/
                using var fileStream = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.None);
                var createResult = fileStream.SafeFileHandle;

                var setFilePointerResult = PInvoke.SetFilePointerEx((HANDLE)createResult.DangerousGetHandle(), (long)size, (long*)IntPtr.Zero, SET_FILE_POINTER_MOVE_METHOD.FILE_BEGIN);
                var endOfFileResult = PInvoke.SetEndOfFile(createResult);

                IO_STATUS_BLOCK ioStatusBlock, ioStatusBlock2;

                var setFileInfoResult = Windows.Wdk.PInvoke.NtSetInformationFile(
                    (HANDLE)createResult.DangerousGetHandle(),
                    &ioStatusBlock,
                    &allocInfo,
                    (uint)sizeof(FILE_ALLOCATION_INFO),
                    FILE_INFORMATION_CLASS.FileAllocationInformation);

                var setFileDataLengthResult = Windows.Wdk.PInvoke.NtSetInformationFile(
                    (HANDLE)createResult.DangerousGetHandle(),
                    &ioStatusBlock2,
                    &dlInfo,
                    (uint)sizeof(FILE_VALID_DATA_LENGTH_INFORMATION),
                    FILE_INFORMATION_CLASS.FileValidDataLengthInformation);
                
                // Happens implicitly by using FileStream (IDisposable)
                bool closeResult = true;
                // It might be a good idea to check if handle is valid at all before closing it. If I ever return to
                // manual PInvoke handles
                //var closeResult = PInvoke.CloseHandle((HANDLE)createResult.DangerousGetHandle());
                
                bool isSuccess = closeResult && !createResult.IsInvalid && setFilePointerResult &&
                    endOfFileResult && setFileDataLengthResult == 0 && setFileInfoResult == 0;

                var result = new CreateFileFastUnsafeResult(!createResult.IsInvalid, setFilePointerResult,
                    endOfFileResult, setFileInfoResult.ToString(), setFileDataLengthResult.ToString(),
                    closeResult, isSuccess);
                
                logger.Debug(result.ToString().EscapeMarkup());

                return result;
            }
        }

        [SupportedOSPlatform("windows5.1.2600")]
        public static bool? IsSparseFile(string target, Log.ILog logger)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger.Error("This call is supported only on Windows");
                return null;
            }

            uint attributes = PInvoke.GetFileAttributes(target);

            if (attributes == 0xFFFFFFFF)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
                catch (Exception e) when (Program.ExceptionFilter(e))
                {
                    logger.Error(e.Message);
                    return null;
                }
            }

            return (attributes & (uint)FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_SPARSE_FILE) != 0;
        }

        public static Flow<IEnumerable<PhysicalVolumeInfo>> GetVolumeList(ILog logger)
        {
            try
            {
                ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher("root\\CIMV2", "SELECT DeviceID, Index, BytesPerSector, Model FROM Win32_DiskDrive");

                DiscUtils.PhysicalVolumeInfo[] GetVolumes(string diskId)
                {
                    using var stream = new BlockDeviceUnbufferedStream(diskId);
                    return VolumeManager.GetPhysicalVolumes(stream);
                }

                string[] GetMountPoints(string diskIdx, int volumeIdx)
                {
                    return Utils.SuppressRef<ManagementException, string[]>(() =>
                    {
                        var query =
                            $"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskIdx} AND PartitionNumber = {volumeIdx + 1}";

                        var obj = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", query)
                            .Get().Cast<ManagementObject>().FirstOrDefault();

                        return obj?["AccessPaths"] as string[] ?? [];
                    }) ?? [];
                }

                var disks = searcher.Get().Cast<ManagementObject>()
                    .AsParallel().AsOrdered()
                    .Select(o => (diskId: o["DeviceID"]?.ToString(), diskIdx: o["Index"]?.ToString(),
                        bytesPerSector: (UInt32)o["BytesPerSector"], hardwareModel: o["Model"]?.ToString()))
                    .Where(d => d is { diskId: not null, diskIdx: not null })
                    .Select(d => (d.hardwareModel, d.diskId, d.diskIdx, d.bytesPerSector, volumes: GetVolumes(d.diskId!)));

                var volumes = disks
                    .SelectMany(d => d.volumes.Select((v, volumeIdx) => (
                        volumeIdx,
                        d.diskId,
                        d.diskIdx,
                        d.bytesPerSector,
                        d.hardwareModel,
                        volumeInfo: v,
                        mountPoints: GetMountPoints(d.diskIdx!, volumeIdx))))
                    .Select(v => new PhysicalVolumeInfo(
                        $"\\\\.\\Harddisk{v.diskIdx}Partition{v.volumeIdx + 1}",
                        v.mountPoints.ToList(),
                        (ulong)v.volumeInfo.Length,
                        v.bytesPerSector, 
                        v.hardwareModel,
                        v.diskIdx?.ToString()));

                return Flows.Val(volumes.ToList().AsEnumerable());
            }
            catch (ManagementException ex)
            {
                logger.Error($"An error occurred during the WMI query: {ex.Message}");

                if (ex.Message.Contains("Access denied"))
                {
                    logger.Warning("--> Hint: Please run this application as Administrator.");
                }

                return new(ex.Message);
            }
            catch (Exception ex) when (Program.ExceptionFilter(ex))
            {
                logger.Error($"An unexpected error occurred: {ex.Message}");

                return new(ex.Message);
            }
        }

        // Can't find a way to do this properly
        private static string CrudeNormalizeDeviceId(string query)
        {
            var result = query;

            if (result.StartsWith(@"\\.\"))
            {
                result = $@"\\?\{result.Substring(4)}";
            }

            return (result.TrimEnd('\\') + @"\").Replace(@"\", @"\\");
        }

        public static long? GetVolumeCapacity(string volumeId)
        {
            if (volumeId.StartsWith(@"\\.\") && volumeId.Length <= @"\\.\C:\".Length)
            {
                volumeId = volumeId.Substring(@"\\.\".Length);
            }
            
            var query = volumeId.Length <= 3 // "C:\" and other variants 
                ? $"SELECT Capacity FROM Win32_Volume WHERE DriveLetter = \"{volumeId.Substring(0, 1).ToUpper()}:\"" 
                : $"SELECT Capacity FROM Win32_Volume WHERE DeviceID = \"{CrudeNormalizeDeviceId(volumeId)}\"";
            
            var obj = new ManagementObjectSearcher(@"root\CIMV2", query)
                .Get().Cast<ManagementObject>().FirstOrDefault();

            if (!obj.IsSome()) return null;
            
            var maybeCapacity = obj?["Capacity"];

            if (maybeCapacity.IsSome())
            {
                return (long)(ulong)maybeCapacity!;
            }

            return null;
        }

        public static Stream OpenVolumeByDeviceIdReadOnly(string deviceId, ILog logger)
        {
            return new BlockDeviceUnbufferedStream(deviceId);
        }

        [SuppressMessage("ReSharper", "ConvertToLambdaExpression")]
        public static Flow<None> CloneVolumeToVirtualDiskWithVss(string volume, string target, ILog logger, bool createFixed = false, bool forceZeroFill = false, bool bootable = false, bool vhdx = false)
        {
            var findVolumes = (IEnumerable<VhdMounter.VhdVolumeInfo> volumes) =>
            {
                const char efiTargetLetter = 'X'; // TODO: ideally, check that it is not already taken

                var vhdVolumeInfos = volumes.ToList();
                var efiVolume = vhdVolumeInfos.Where(v => v.FileSystem == "FAT32").FirstOrNone();
                var dataVolume = vhdVolumeInfos.Where(v => v.FileSystem == "NTFS").FirstOrNone();

                return efiVolume
                    .Concat(dataVolume)
                    .Convert(x => new {efi = x.Item1, data = x.Item2, efiTargetLetter})
                    .Flow($"Failed to detect/find EFI/data volumes");
            };

            var request = new { volume, target, createFixed, forceZeroFill, bootable, vhdx, logger };
            
            // Pinned to the scope, as there are IDisposable values inside: Backup and BlockDeviceUnbufferedStream
            using var result = Flows.Val(request)
                .BindConcat(opts => Vss.Backup.Make(opts.volume, opts.logger), (opts, vss) => new { opts, vss })
                .Handle((Exception e) => e.Message)
                .MapConcat(state => new BlockDeviceUnbufferedStream(state.vss.Root, true), (state, snapStream) => new { state.vss, state.opts, snapStream })
                .PopCtx()
                .LogOk(logger, state => $"Created snapshot {state.vss.Root} for volume {state.opts.volume}")
                .BindErr(state => DiskImage.Util.CreateVdiskWithGptLayout(
                    state.opts.target, state.opts.bootable ? 512UL * 1024 * 1024 : 0UL, (ulong)state.snapStream.Length, 
                    state.opts.logger, state.opts.forceZeroFill, !state.opts.createFixed, state.opts.vhdx))
                .BindErr(state =>
                {
                    using var disk = VirtualDisk.OpenDisk(state.opts.target, FileAccess.ReadWrite);
                    var partStream = disk.Partitions.Partitions[state.opts.bootable ? 1 : 0].Open();
                    
                    return NtfsClone.Clone(state.snapStream, partStream , state.opts.logger)
                        .HandleAll()
                        .BindErrIf(_ => state.opts.bootable, [SuppressMessage("ReSharper", "AccessToDisposedClosure")] (_) =>
                        {
                            logger.Log($"Formatting EFI partition into FAT32");
                            FatFileSystem.FormatPartition(disk, 0, "BOOT");
                            return Flows.Val(None.Value);
                        })
                        .PopCtx();
                })
                .BindErrIf(state => state.opts.bootable, state => // If requested, prepare boot files on EFI partition using bcdboot
                {
                    return VhdMounter.Mount(state.opts.target, state.opts.logger)
                        .Bind(findVolumes)
                        .BindErr(v => DriveLetterManager.AddDriveLetterToVolume(v.efi.Path, v.efiTargetLetter, state.opts.logger))
                        .Bind(v => CliTools.ExecuteBcdBoot(v.efiTargetLetter, v.data.DriveLetter ?? 'C', state.opts.logger)) // ugly fallback to C, but I don't have better solution at the moment
                        .Bind(l => DriveLetterManager.RemoveDriveLetterFromVolume(l.Val, state.opts.logger))
                        .Check(_ => VhdMounter.Dismount(state.opts.target, state.opts.logger), (_) => $"Failed to dismount {state.opts.target}");
                })
                .LogOk(logger, state => $"Closing snapshot {state.vss.Root} for volume {state.opts.volume}")
                .MapDispose(state => new {state.opts, state.vss}, state => state.snapStream)
                .MapDispose(state => state.opts, state => state.vss);

            return result.IsErr ? new(result.UnwrapErr()) : Flows.Val(None.Value);
        }
    }
}
#endif