using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    class OffsetStreamDecorator : Stream
    {
        private readonly Stream _instance;
        private readonly long _offset;
        private readonly long _trim;

        public OffsetStreamDecorator(FileStream instance, long offset, long trim)
        {
            this._instance = instance;
            this._offset = offset;
            this._trim = trim;
        }

        public override long Length
        {
            get { return _instance.Length - _offset - _trim; }
        }

        public override void SetLength(long value)
        {
            _instance.SetLength(value + _offset);
        }

        public override long Position
        {
            get { return _instance.Position - this._offset; }
            set { _instance.Position = value + this._offset; }
        }

        public override bool CanRead => _instance.CanRead;

        public override bool CanSeek => _instance.CanSeek;

        public override bool CanWrite => _instance.CanWrite;

        public override IAsyncResult BeginRead(byte[] array, int offset, int numBytes, AsyncCallback? userCallback, object? stateObject)
        {
            return _instance.BeginRead(array, offset, numBytes, userCallback, stateObject);
        }

        public override IAsyncResult BeginWrite(byte[] array, int offset, int numBytes, AsyncCallback? userCallback, object? stateObject)
        {
            return _instance.BeginWrite(array, offset, numBytes, userCallback, stateObject);
        }

        public override void Flush()
        {
            _instance.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _instance.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _instance.Seek(offset, origin);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _instance.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            _instance.Dispose();
            base.Dispose(disposing);
        }
    }
}
