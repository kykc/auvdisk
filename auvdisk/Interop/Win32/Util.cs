using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils.Streams;
using DotNext;

#if WINDOWS
using System.Management;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Wdk.Storage.FileSystem;
using Windows.Wdk.System.SystemServices;
using Windows.Win32.System.IO;
using DiscUtils;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;


namespace auvdisk.Interop.Win32
{
    [SupportedOSPlatform("windows5.1.2600")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Util
    {
        public record CreateFileFastUnsafeResult(bool HandleCreated, bool SetFilePointerResult, bool SetEndOfFileResult,
            string SetFileAllocationInfoResult, string SetFileDataLengthResult, bool CloseResult, bool IsSuccess);

        [SupportedOSPlatform("windows5.1.2600")]
        public static CreateFileFastUnsafeResult ResizeFileFastUnsafe(string target, ulong size, Log.ILog logger)
        {
            unsafe
            {
                FILE_VALID_DATA_LENGTH_INFORMATION dlInfo = new FILE_VALID_DATA_LENGTH_INFORMATION();
                dlInfo.ValidDataLength = (long)size;

                FILE_ALLOCATION_INFO allocInfo = new FILE_ALLOCATION_INFO();
                allocInfo.AllocationSize = (long)size;

                var createResult = PInvoke.CreateFile(
                    target,
                    (uint)GENERIC_ACCESS_RIGHTS.GENERIC_WRITE,
                    0,
                    lpSecurityAttributes: null,
                    FILE_CREATION_DISPOSITION.OPEN_ALWAYS,
                    0,
                    hTemplateFile:
                    null);

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

                var closeResult = PInvoke.CloseHandle((HANDLE)createResult.DangerousGetHandle());

                bool isSuccess = closeResult && !createResult.IsInvalid && setFilePointerResult &&
                    endOfFileResult && setFileDataLengthResult == 0 && setFileInfoResult == 0;

                var result = new CreateFileFastUnsafeResult(!createResult.IsInvalid, setFilePointerResult,
                    endOfFileResult, setFileInfoResult.ToString(), setFileDataLengthResult.ToString(),
                    closeResult, isSuccess);

                logger.Log(Markup.Escape(result.ToString()));

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
                    new ManagementObjectSearcher("root\\CIMV2", "SELECT DeviceID, Index, BytesPerSector FROM Win32_DiskDrive");

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
                        bytesPerSector: (UInt32)o["BytesPerSector"]))
                    .Where(d => d is { diskId: not null, diskIdx: not null })
                    .Select(d => (d.diskId, d.diskIdx, d.bytesPerSector, volumes: GetVolumes(d.diskId!)));

                var volumes = disks
                    .SelectMany(d => d.volumes .Select((v, volumeIdx) => (
                        volumeIdx,
                        d.diskId,
                        d.diskIdx,
                        d.bytesPerSector,
                        volumeInfo: v,
                        mountPoints: GetMountPoints(d.diskIdx!, volumeIdx))))
                    .Select(v => new PhysicalVolumeInfo(
                        $"\\\\.\\Harddisk{v.diskIdx}Partition{v.volumeIdx + 1}",
                        v.mountPoints?.ToList() ?? [],
                        (ulong)v.volumeInfo.Length,
                        v.bytesPerSector));

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
    }
}
#endif