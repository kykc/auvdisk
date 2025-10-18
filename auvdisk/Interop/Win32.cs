using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

#if WINDOWS
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;


namespace auvdisk.Interop
{
    [SupportedOSPlatform("windows5.1.2600")]
    internal static class Win32
    {
        [SupportedOSPlatform("windows5.1.2600")]
        public static void CreateFileFastUnsafe(string target, ulong size)
        {
            /* Equivalent of the following C code
            HANDLE hf = CreateFile(filePath, GENERIC_WRITE, 0, 0, CREATE_ALWAYS, 0, 0);
            auto resultFilePointer = SetFilePointerEx(hf, size, 0, FILE_BEGIN);
            auto status = GetLastError();
            auto resultEndOfFile = SetEndOfFile(hf);
            auto resultClose = CloseHandle(hf);
            */

            unsafe
            {
                var createResult = PInvoke.CreateFile(target, (uint)GENERIC_ACCESS_RIGHTS.GENERIC_WRITE, 0, lpSecurityAttributes: null, FILE_CREATION_DISPOSITION.CREATE_ALWAYS, 0, hTemplateFile: null);
                var setFilePointerResult = PInvoke.SetFilePointerEx(createResult, (long)size, (long*)IntPtr.Zero, SET_FILE_POINTER_MOVE_METHOD.FILE_BEGIN);
                var endOfFileResult = PInvoke.SetEndOfFile(createResult);
                var closeResult = PInvoke.CloseHandle((HANDLE)createResult.DangerousGetHandle());
            }
        }

        public static bool IsSparseFile(string target)
        {
            throw new NotImplementedException();
        }
    }
}
#endif