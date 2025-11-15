using System.Buffers;
using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.Bytes
{
    public sealed class StreamCopyProgressWrapper(Stream subject) : Stream, IDisposable
    {
        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        public const int DefaultCopyBufferSize = 81920;
        
        public class ProgressData(string actionName, long totalBytes) : IProgressData
        {
            public long TotalBytes { get; init; } = totalBytes;
            public int IncrementBytes { get; set; } = 0;
            public string Description => $"{actionName}...";
            public string Complete => "Done.";
        }

        public void ZeroFill(ulong sectorCount, int sectorSize, ILog logger)
        {
            var progressData = new ProgressData("Zero-filling", (long)sectorCount * sectorSize);
            
            byte[] nullSector = Enumerable.Repeat((byte)0x0, sectorSize).ToArray();

            // TODO: rebuffer to (larger) buffer size
            Utils.WithProgress(logger, progressData, (progress) =>
            {
                for (ulong sector = 0; sector < sectorCount; ++sector)
                {
                    Write(nullSector);
                    
                    progressData.IncrementBytes += nullSector.Length;
                    progress?.Call(progressData);
                }

                return progressData;
            });
        }

        [Obsolete("Use CopyTo(destination, logger) instead.")]
        public new void CopyTo(Stream destination)
        {
            throw new InvalidOperationException();
        }
        
        public void CopyTo(Stream destination, ILog logger, int bufferSize = DefaultCopyBufferSize)
        {
            var progressData = new ProgressData("Copying", Length);
            
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                throw new NotSupportedException("Stream does not support reading.");
            }
           
            Utils.WithProgress(logger, progressData, progress =>
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    int bytesRead;
                    while ((bytesRead = Read(buffer, 0, buffer.Length)) != 0)
                    {
                        destination.Write(buffer, 0, bytesRead);
                        progressData.IncrementBytes += bytesRead;
                        progress?.Call(progressData);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return progressData;
            });
        }
        
        public override void Flush()
        {
            subject.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return subject.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return subject.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            subject.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            subject.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                subject.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => subject.CanRead;
        public override bool CanSeek => subject.CanSeek;
        public override bool CanWrite => subject.CanWrite;
        public override long Length => subject.Length;
        public override long Position
        {
            get => subject.Position;
            set => subject.Position = value;
        }
    }
}