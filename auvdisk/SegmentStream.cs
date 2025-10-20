namespace auvdisk
{
    public class SegmentStream : Stream
    {
        private readonly Stream _parentStream;
        private readonly long _offset;
        private readonly long _length;
        
        private long _currentRelativePosition;

        /// <summary>
        /// Initializes a new instance of the SegmentStream class.
        /// </summary>
        /// <param name="parentStream">The underlying stream to wrap.</param>
        /// <param name="offset">The starting offset (absolute position) in the parent stream.</param>
        /// <param name="length">The length of the segment.</param>
        /// <exception cref="ArgumentNullException">Thrown if parentStream is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the parent stream cannot seek or read, or if offset/length are invalid.</exception>
        public SegmentStream(Stream parentStream, long offset, long length)
        {
            _parentStream = parentStream ?? throw new ArgumentNullException(nameof(parentStream));
            
            if (!_parentStream.CanSeek)
                throw new ArgumentException("Parent stream must support seeking.", nameof(parentStream));
            if (!_parentStream.CanRead)
                throw new ArgumentException("Parent stream must support reading.", nameof(parentStream));
            
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
            
            // Ensure the segment is within the bounds of the parent stream
            if (offset + length > _parentStream.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "Segment exceeds the length of the parent stream.");

            _offset = offset;
            _length = length;
            _currentRelativePosition = 0;

            // We don't seek the parent stream here; we rely on the Read and Seek methods 
            // to manage the parent stream's position relative to _offset.
        }

        public override bool CanRead => true;
        public override bool CanSeek => _parentStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _currentRelativePosition;
            set => Seek(value, SeekOrigin.Begin);
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0 || _currentRelativePosition >= _length)
            {
                return 0; // End of the segment reached
            }

            // 1. Calculate the maximum number of bytes available to read in the segment
            long bytesRemainingInSegment = _length - _currentRelativePosition;

            // 2. Clamp the requested count to the available remaining bytes
            int actualCountToRead = (int)Math.Min(count, bytesRemainingInSegment);

            // 3. Set the parent stream's position to the absolute start of the read operation
            long absoluteReadStart = _offset + _currentRelativePosition;
            _parentStream.Position = absoluteReadStart;

            // 4. Read from the parent stream
            int bytesRead = _parentStream.Read(buffer, offset, actualCountToRead);

            // 5. Update the relative position within this segment
            _currentRelativePosition += bytesRead;

            return bytesRead;
        }
        
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!_parentStream.CanSeek)
            {
                throw new NotSupportedException("The parent stream does not support seeking.");
            }

            long newRelativePosition;

            // 1. Calculate the new relative position based on origin
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newRelativePosition = offset;
                    break;
                case SeekOrigin.Current:
                    newRelativePosition = _currentRelativePosition + offset;
                    break;
                case SeekOrigin.End:
                    newRelativePosition = _length + offset;
                    break;
                default:
                    throw new ArgumentException("Invalid seek origin.", nameof(origin));
            }

            // 2. Validate the new relative position is within segment bounds [0, _length]
            if (newRelativePosition < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Attempted to seek before the start of the segment.");
            }
            if (newRelativePosition > _length)
            {
                // Note: Seeking beyond the length is allowed but should clamp the position
                // or rely on Read to return 0 when beyond length. We'll allow seeking exactly
                // to _length, but not beyond.
                throw new ArgumentOutOfRangeException(nameof(offset), "Attempted to seek beyond the end of the segment.");
            }

            // 3. Update the internal relative position
            _currentRelativePosition = newRelativePosition;

            // Note: We don't need to explicitly seek the parent stream here, as the Read method
            // will set the parent position based on the updated _currentRelativePosition.
            // However, if other code relies on the parent's position being strictly updated on seek, 
            // uncomment the following block:
            /*
            long newAbsolutePosition = _startOffset + _currentRelativePosition;
            _parentStream.Position = newAbsolutePosition;
            */

            return _currentRelativePosition;
        }

        public override void Flush() => _parentStream.Flush();
        
        public override void SetLength(long value)
        {
            throw new NotSupportedException("Setting the length of a segment stream is not supported.");
        }

        /// <summary>
        /// Writing is not supported for this read-only segment stream wrapper.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Writing to a segment stream is not supported.");
        }

        /// <summary>
        /// Ensures the parent stream is not prematurely disposed.
        /// </summary>
        /// <param name="disposing">True if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            // IMPORTANT: Do NOT dispose the _parentStream, as this wrapper doesn't own it.
            // The responsibility of disposing the parent stream lies with the caller.
            base.Dispose(disposing);
        }
    }
}