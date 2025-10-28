using System.Text;

namespace auvdisk.test
{
    public class ListStringWriterStream : Stream
    {
        private readonly List<string> _lines;
        private readonly Encoding _encoding;
        private readonly MemoryStream _buffer = new();

        public ListStringWriterStream(List<string> targetList, Encoding? encoding = null)
        {
            _lines = targetList;
            _encoding = encoding ?? Encoding.UTF8;
        }

        // Write-only stream
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override void Write(byte[] buffer, int offset, int count)
        {
            // Write the new data to our internal memory buffer
            _buffer.Write(buffer, offset, count);
            ProcessBuffer();
        }

        private void ProcessBuffer()
        {
            byte[] allBytes = _buffer.ToArray();
            int lastPos = 0;
            int searchPos = 0;

            while (searchPos < allBytes.Length)
            {
                int newlineIndex = Array.IndexOf(allBytes, (byte)'\n', searchPos);
                if (newlineIndex < 0)
                {
                    break;
                }

                int length = newlineIndex - lastPos;
                if (length > 0 && allBytes[newlineIndex - 1 ] == '\r')
                {
                    length--;
                }

                string line = _encoding.GetString(allBytes, lastPos, length);
                _lines.Add(line);

                lastPos = newlineIndex + 1;
                searchPos = lastPos;
            }

            if (lastPos < allBytes.Length)
            {
                byte[] remaining = new byte[allBytes.Length - lastPos];
                Buffer.BlockCopy(allBytes, lastPos, remaining, 0, remaining.Length);

                _buffer.SetLength(0);
                _buffer.Write(remaining, 0, remaining.Length);
            }
            else
            {
                _buffer.SetLength(0);
            }
        }

        public override void Flush()
        {
            if (_buffer.Length > 0)
            {
                string lastLine = _encoding.GetString(_buffer.ToArray());
                _lines.Add(lastLine);
                _buffer.SetLength(0);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
                _buffer.Dispose();
            }
            base.Dispose(disposing);
        }

        public override long Length => throw new NotSupportedException("This stream does not support seeking or length.");
        public override long Position
        {
            get => throw new NotSupportedException("This stream does not support seeking.");
            set => throw new NotSupportedException("This stream does not support seeking.");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("This stream is write-only.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("This stream does not support seeking.");
        public override void SetLength(long value) => throw new NotSupportedException("This stream does not support setting length.");
    }
}