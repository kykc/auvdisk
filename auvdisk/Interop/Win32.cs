using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

#if WINDOWS
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Wdk.Storage.FileSystem;
using Windows.Wdk.System.SystemServices;
using Windows.Win32.System.IO;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;


namespace auvdisk.Interop
{
    [SupportedOSPlatform("windows5.1.2600")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Win32
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

                var setFilePointerResult = PInvoke.SetFilePointerEx(createResult, (long)size, (long*)IntPtr.Zero, SET_FILE_POINTER_MOVE_METHOD.FILE_BEGIN);
                var endOfFileResult = PInvoke.SetEndOfFile(createResult);

                var setFileInfoResult = Windows.Wdk.PInvoke.NtSetInformationFile(
                    (HANDLE)createResult.DangerousGetHandle(),
                    out IO_STATUS_BLOCK ioStatusBlock,
                    &allocInfo,
                    (uint)sizeof(FILE_ALLOCATION_INFO),
                    FILE_INFORMATION_CLASS.FileAllocationInformation);

                var setFileDataLengthResult = Windows.Wdk.PInvoke.NtSetInformationFile(
                    (HANDLE)createResult.DangerousGetHandle(),
                    out IO_STATUS_BLOCK ioStatusBlock2,
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

        public static bool IsSparseFile(string target)
        {
            throw new NotImplementedException();
        }
    }
}
#endif