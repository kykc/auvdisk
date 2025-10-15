using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    class OffsetStreamDecorator : Stream
    {
        private readonly Stream instance;
        private readonly long offset;
        private readonly long trim;

        public OffsetStreamDecorator(FileStream instance, long offset, long trim)
        {
            this.instance = instance;
            this.offset = offset;
            this.trim = trim;
        }

        public override long Length
        {
            get { return instance.Length - offset - trim; }
        }

        public override void SetLength(long value)
        {
            instance.SetLength(value + offset);
        }

        public override long Position
        {
            get { return instance.Position - this.offset; }
            set { instance.Position = value + this.offset; }
        }

        public override bool CanRead => instance.CanRead;

        public override bool CanSeek => instance.CanSeek;

        public override bool CanWrite => instance.CanWrite;

        public override IAsyncResult BeginRead(byte[] array, int offset, int numBytes, AsyncCallback? userCallback, object? stateObject)
        {
            return instance.BeginRead(array, offset, numBytes, userCallback, stateObject);
        }

        public override IAsyncResult BeginWrite(byte[] array, int offset, int numBytes, AsyncCallback? userCallback, object? stateObject)
        {
            return instance.BeginWrite(array, offset, numBytes, userCallback, stateObject);
        }

        public override void Flush()
        {
            instance.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return instance.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return instance.Seek(offset, origin);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            instance.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            instance.Dispose();
            base.Dispose(disposing);
        }
    }
}
