#if WINDOWS
using auvdisk.Interop;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    internal class FsUtils
    {
#pragma warning disable CA1416
        public static void CreateFileFastUnsafe(string target, ulong size)
        {
#if WINDOWS
            Win32.CreateFileFastUnsafe(target, size);
#else
            var stream = new FileStream(target, FileMode.CreateNew);
            stream.Seek((long)size - 1, SeekOrigin.Begin);
            stream.WriteByte(0);
            stream.Close();
#endif
        }
#pragma warning restore CA1416
    }
}
