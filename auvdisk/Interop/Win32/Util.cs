#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using auvdisk.Cli;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils.Fat;
using DiscUtils.Streams;
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
                
                logger.Debug(Markup.Escape(result.ToString()));

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
                catch (Exception e)
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

                string[]? GetMountPoints(string diskIdx, int volumeIdx)
                {
                    return Utils.SuppressRef<ManagementException, string[]>(() =>
                    {
                        var query =
                            $"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskIdx} AND PartitionNumber = {volumeIdx + 1}";

                        var obj = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", query)
                            .Get().Cast<ManagementObject>().FirstOrDefault();

                        return obj?["AccessPaths"] as string[] ?? [];
                    });
                }

                var disks = searcher.Get().Cast<ManagementObject>()
                    .AsParallel().AsOrdered()
                    .Select(o => (diskId: o["DeviceID"]?.ToString(), diskIdx: o["Index"]?.ToString(),
                        bytesPerSector: (UInt32)o["BytesPerSector"], hardwareModel: o["Model"]?.ToString()))
                    .Where(d => d is { diskId: not null, diskIdx: not null })
                    .Select(d => (d.hardwareModel, d.diskId, d.diskIdx, d.bytesPerSector, volumes: GetVolumes(d.diskId!)));

                var volumes = disks
                    .SelectMany(d => d.volumes .Select((v, volumeIdx) => (
                        volumeIdx,
                        d.diskId,
                        d.diskIdx,
                        d.bytesPerSector,
                        d.hardwareModel,
                        volumeInfo: v,
                        mountPoints: GetMountPoints(d.diskIdx!, volumeIdx))))
                    .Select(v => new PhysicalVolumeInfo(
                        $"\\\\.\\Harddisk{v.diskIdx}Partition{v.volumeIdx + 1}",
                        v.mountPoints?.ToList() ?? [],
                        (ulong)v.volumeInfo.Length,
                        v.bytesPerSector, 
                        v.hardwareModel,
                        v.diskIdx?.ToString()));

                return Flow<IEnumerable<PhysicalVolumeInfo>>.Ok(volumes.ToList(), logger);
            }
            catch (ManagementException ex)
            {
                logger.Error($"An error occurred during the WMI query: {ex.Message}");

                if (ex.Message.Contains("Access denied"))
                {
                    logger.Warning("--> Hint: Please run this application as Administrator.");
                }

                return Flow<IEnumerable<PhysicalVolumeInfo>>.Err(ex.Message, logger);
            }
            catch (Exception ex)
            {
                logger.Error($"An unexpected error occurred: {ex.Message}");

                return Flow<IEnumerable<PhysicalVolumeInfo>>.Err(ex.Message, logger);
            }
        }

        public static Stream OpenVolumeByDeviceIdReadOnly(string deviceId, ILog logger)
        {
            return new BlockDeviceUnbufferedStream(deviceId);
        }

        [SuppressMessage("ReSharper", "ConvertToLambdaExpression")]
        public static Flow<Value<ulong>> CloneVolumeToVirtualDiskWithVss(string volume, string target, ILog logger, bool createFixed = false, bool forceZeroFill = false, bool bootable = false, bool vhdx = false)
        {
            var findVolumes = (IEnumerable<VhdMounter.VhdVolumeInfo> volumes) =>
            {
                const char efiTargetLetter = 'X'; // TODO: ideally, check that it is not already taken
                
                var efiVolume = volumes.Where(v => v.FileSystem == "FAT32").FirstOrNone();
                var dataVolume = volumes.Where(v => v.FileSystem == "NTFS").FirstOrNone();

                return efiVolume
                    .Concat(dataVolume)
                    .Convert(x => new {efi = x.Item1, data = x.Item2, efiTargetLetter})
                    .Flow($"Failed to detect/find EFI/data volumes", logger);
            };
            
            Vss.Backup? vss = null;
            Stream? snapStream = null;
            
            return 
                Vss.Backup.Make(volume, logger) // Create VSS session
                .WithSideEffect(vssObj => // pin VSS session and volume stream, need to be very careful with their guaranteed disposal
                {
                    vss = vssObj;
                    snapStream = new BlockDeviceUnbufferedStream(vss.Root);
                    logger.Log($"Created snapshot {vss.Root} for volume {volume}");
                })
                .Bind(_ => // Create VHD/VHDx file, prepare target partitions
                {
                    return DiskImage.Util.CreateBootableLayout(
                        target, 512 * 1024 * 1024, (ulong)snapStream!.Length, 
                        logger, forceZeroFill, !createFixed, vhdx);
                })
                .WithSideEffect(_ => // Open image with DiscUtils, format EFI partition, clone NTFS partition
                {
                    using var disk = VirtualDisk.OpenDisk(target, FileAccess.ReadWrite);
                    logger.Log($"Formatting EFI partition into FAT32");
                    FatFileSystem.FormatPartition(disk, 0, "BOOT");
                    var partStream = disk.Partitions.Partitions[1].Open();

                    NtfsClone.Clone(snapStream!, partStream , logger);
                })
                .CheckDiscardIf(_ => bootable, _ => // If requested, prepare boot files on EFI partition using bcdboot
                {
                    return VhdMounter.Mount(target, logger)
                        .Bind(findVolumes)
                        .CheckDiscard(v => DriveLetterManager.AddDriveLetterToVolume(v.efi.Path, v.efiTargetLetter, logger))
                        .Bind(v => CliTools.ExecuteBcdBoot(v.efiTargetLetter, v.data.DriveLetter ?? 'C', logger)) // ugly fallback to C, but I don't have better solution at the moment
                        .Bind(l => DriveLetterManager.RemoveDriveLetterFromVolume(l.Val, logger))
                        .Check(_ => VhdMounter.Dismount(target, logger), (_) => $"Failed to dismount {target}");
                })
                .Finally(() =>
                {
                    if (vss != null)
                    {
                        logger.Log($"Closing snapshot {vss.Root} for volume {volume}");
                    }

                    snapStream?.Dispose();
                    vss?.Dispose();
                });
        }
    }
}
#endif