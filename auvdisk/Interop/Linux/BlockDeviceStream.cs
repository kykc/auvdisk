using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace auvdisk.Interop.Linux
{
    sealed class BlockDeviceStream : FileStream
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private const uint BLKGETSIZE64 = 0x80081272;

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref long data);

        public BlockDeviceStream(string path) : base(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
        {
            // Get the file descriptor from SafeFileHandle
            int fd = SafeFileHandle.DangerousGetHandle().ToInt32();
            long size = 0;

            // Call ioctl to get device size
            int result = ioctl(fd, BLKGETSIZE64, ref size);

            if (result == -1)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new IOException($"ioctl BLKGETSIZE64 failed with errno: {errno}");
            }

            Length = size;
        }

        public override long Length { get; }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Cannot set length of a block device");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("This stream is read-only");
        }

        public override bool CanWrite => false;
    }
}