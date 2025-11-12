using System.Buffers;
using auvdisk.Extensions;
using ShellProgressBar;

namespace auvdisk.Bytes
{
    public class StreamCopyProgressWrapper(Stream subject) : Stream
    {
        public const int DefaultCopyBufferSize = 81920;
        
        public class ProgressData
        {
            public long TotalBytes { get; init; } = 0;
            public long ProcessedBytes { get; set; } = 0;
            public double PercentComplete => TotalBytes > 0 ? ProcessedBytes * 1.0 / TotalBytes : 100.0;
        }

        public class ProgressOptions
        {
            public string ActionName { get; set; } = "Copying";
            public string ProgressName { get; set; } = "Copied";
            public int Ticks { get; set; } = 10000;
            public bool Enabled { get; set; } = Program.IsInteractive;
        }

        public IProgress<ProgressData> GetProgress(ProgressBar progressBar, string actionName = "Copied")
        {
            return progressBar.AsProgress<ProgressData>(
                t => $"{actionName} {t.ProcessedBytes.HumanizeBytes()} of {t.TotalBytes.HumanizeBytes()}", 
                t => t.PercentComplete);
        }

        public void ZeroFill(ulong sectorCount, int sectorSize, ProgressOptions progOpts)
        {
            ProgressBar? progressBar = null;
            IProgress<ProgressData>? progress = null;

            if (progOpts.Enabled)
            {
                // ProgressBar seems to be mangling few last lines on the terminal
                Console.WriteLine();
                Console.Out.Flush();
                
                progressBar = new ProgressBar(progOpts.Ticks, $"{progOpts.ActionName}...");
                progress = GetProgress(progressBar);
            }

            var throttle = new Throttle<ProgressData>(
                (p) => progress?.Report(p), 
                Program.ProgressReportRate);
            
            var progressData = new ProgressData
            {
                TotalBytes = (long)sectorCount * sectorSize,
                ProcessedBytes = 0
            };
            
            byte[] nullSector = Enumerable.Repeat((byte)0x0, sectorSize).ToArray();
            
            for (ulong sector = 0; sector < sectorCount; ++sector)
            {
                Write(nullSector);
                
                progressData.ProcessedBytes += nullSector.Length;
                throttle.Call(progressData);
            }
            
            progress?.Report(progressData);
            progressBar?.Dispose();
        }

        public void CopyTo(Stream destination, ProgressOptions options)
        {
            if (options.Enabled)
            {
                // ProgressBar seems to be mangling few last lines on the terminal
                Console.WriteLine();
                Console.Out.Flush();
                
                using var progressBar = new ProgressBar(options.Ticks, $"{options.ActionName}...");

                CopyTo(destination, GetProgress(progressBar, options.ProgressName));
            }
            else
            {
                base.CopyTo(destination);
            }
        }
        
        protected void CopyTo(Stream destination, IProgress<ProgressData>? progress)
        {
            int bufferSize = DefaultCopyBufferSize;
            
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                throw new NotSupportedException("Stream does not support reading.");
            }
            
            var throttle = new Throttle<ProgressData>(
                (p) => progress?.Report(p), 
                Program.ProgressReportRate);
            
            var progressData = new ProgressData
            {
                TotalBytes = Length,
                ProcessedBytes = 0
            };

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = Read(buffer, 0, buffer.Length)) != 0)
                {
                    destination.Write(buffer, 0, bytesRead);
                    progressData.ProcessedBytes += bytesRead;
                    throttle.Call(progressData);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                progress?.Report(progressData);
            }
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