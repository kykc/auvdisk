using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#if WINDOWS
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;
using Microsoft.Win32.SafeHandles;

namespace auvdisk.Interop.Win32
{
    [SupportedOSPlatform("windows5.1.2600")]
    public class BlockDeviceUnbufferedStream : Stream
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;

        private const int BufferSize = 64 * 1024;
        // TODO: read alignment from IOCTL? https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_disk_get_drive_geometry
        // I also have this info in PhysicalVolumeInfo now
        private const int Alignment = 512;

        private long _position;
        private readonly SafeFileHandle _handle;
        private IntPtr _bufferAllocHandle;
        private readonly IntPtr _buffer;

        public BlockDeviceUnbufferedStream(string path) :
            this(Windows.Win32.PInvoke.CreateFile(
                path,
                (uint)FileAccess.Read,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                0,
                null))
        {
            //if (!NativeMethods.DeviceIoControl(_handle, NativeMethods.EIOControlCode.FsctlAllowExtendedDasdIo, null, 0, out _, 0, out _, IntPtr.Zero))
        }

        public BlockDeviceUnbufferedStream(SafeFileHandle handle)
        {
            _handle = handle;

            if (_handle.IsInvalid)
            {
                throw new Win32Exception();
            }

            _bufferAllocHandle = Marshal.AllocHGlobal(BufferSize + Alignment);
            _buffer = new IntPtr(((_bufferAllocHandle.ToInt64() + Alignment - 1) / Alignment) * Alignment);

            _position = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (_bufferAllocHandle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferAllocHandle);
                _bufferAllocHandle = IntPtr.Zero;
            }

            if (!_handle.IsClosed)
            {
                _handle.Close();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override long Length
        {
            get
            {
                unsafe
                {
                    var buffer = new byte[sizeof(GET_LENGTH_INFORMATION)];
                    if (!Windows.Win32.PInvoke.DeviceIoControl(_handle, IOCTL_DISK_GET_LENGTH_INFO, null, buffer, null))
                    {
                        throw new Win32Exception();
                    }
                    GET_LENGTH_INFORMATION lengthInfo = Bytes.Util.Deserialize<GET_LENGTH_INFORMATION>(buffer);
                    // TODO: if I want this to be more generic it might be a good idea to fallback to GetFileSizeEx here

                    return lengthInfo.Length;
                }
            }
        }

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override unsafe int Read(Span<byte> buffer)
        {
            var totalBytesRead = 0;
            var length = Length;

            while (totalBytesRead < buffer.Length)
            {
                var alignedStart = (_position / Alignment) * Alignment;
                var alignmentOffset = (int)(_position - alignedStart);

                if (!Windows.Win32.PInvoke.SetFilePointerEx(_handle, alignedStart, out var newPos, 0))
                {
                    throw new Win32Exception();
                }

                var toRead = (int)Math.Min(length - alignedStart, BufferSize);
                uint numRead;
                if (!Windows.Win32.PInvoke.ReadFile((Windows.Win32.Foundation.HANDLE)_handle.DangerousGetHandle(), (byte*)_buffer.ToPointer(), (uint)toRead, &numRead, null))
                {
                    throw new Win32Exception();
                }

                var usefulData = numRead - alignmentOffset;
                if (usefulData <= 0)
                {
                    return totalBytesRead;
                }

                var toCopy = Math.Min(buffer.Length - totalBytesRead, usefulData);

                new ReadOnlySpan<byte>((_buffer + alignmentOffset).ToPointer(), (int)toCopy).CopyTo(buffer.Slice(totalBytesRead));

                totalBytesRead += (int)toCopy;
                _position += toCopy;
            }

            return totalBytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var effectiveOffset = offset;
            if (origin == SeekOrigin.Current)
            {
                effectiveOffset += _position;
            }
            else if (origin == SeekOrigin.End)
            {
                effectiveOffset += Length;
            }

            if (effectiveOffset < 0)
            {
                throw new IOException("Attempt to move before beginning of disk");
            }
            else
            {
                _position = effectiveOffset;
                return _position;
            }
        }

        public sealed override bool CanWrite => false;
        public sealed override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Attempt to write to read-only stream");
        public sealed override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Attempt to write to read-only stream");
        public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new InvalidOperationException("Attempt to write to read-only stream");
        public sealed override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Attempt to write to read-only stream");
        public sealed override void WriteByte(byte value) => throw new InvalidOperationException("Attempt to write to read-only stream");
        public sealed override void Flush() { }
        public sealed override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public sealed override void SetLength(long value) => throw new InvalidOperationException("Attempt to change length of read-only stream");
    }
}
#endif