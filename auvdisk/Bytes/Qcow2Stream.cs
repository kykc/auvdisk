using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using auvdisk.Extensions;

namespace auvdisk.Bytes
{
    public class Qcow2Stream : Stream
    {
        private readonly Stream _baseStream;
        private readonly Qcow2Header _header;
        private readonly ulong[] _l1Table;
        private long _position;

        public Qcow2Stream(string path)
            : this(File.OpenRead(path))
        {
        }

        public Qcow2Stream(Stream baseStream)
        {
            _baseStream = baseStream;
            _header = ReadHeader();
            _l1Table = ReadL1Table();
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => (long)_header.Size;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > Length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        private Qcow2Header ReadHeader()
        {
            _baseStream.Position = 0;
            var headerBytes = new byte[104]; // Minimum header size
            _baseStream.ReadExactly(headerBytes, 0, headerBytes.Length);

            var header = new Qcow2Header
            {
                Magic = ReadBigEndianUInt32(headerBytes, 0),
                Version = ReadBigEndianUInt32(headerBytes, 4),
                BackingFileOffset = ReadBigEndianUInt64(headerBytes, 8),
                BackingFileSize = ReadBigEndianUInt32(headerBytes, 16),
                ClusterBits = ReadBigEndianUInt32(headerBytes, 20),
                Size = ReadBigEndianUInt64(headerBytes, 24),
                CryptMethod = ReadBigEndianUInt32(headerBytes, 32),
                L1Size = ReadBigEndianUInt32(headerBytes, 36),
                L1TableOffset = ReadBigEndianUInt64(headerBytes, 40),
                RefcountTableOffset = ReadBigEndianUInt64(headerBytes, 48),
                RefcountTableClusters = ReadBigEndianUInt32(headerBytes, 56),
                NbSnapshots = ReadBigEndianUInt32(headerBytes, 60),
                SnapshotsOffset = ReadBigEndianUInt64(headerBytes, 64)
            };

            if (header.Magic != 0x514649fb) // "QFI\xfb"
                throw new InvalidDataException("Invalid QCOW2 magic number");

            if (header.Version < 2 || header.Version > 3)
                throw new NotSupportedException($"QCOW2 version {header.Version} not supported");

            if (header.CryptMethod != 0)
                throw new NotSupportedException("Encrypted QCOW2 images are not supported");

            header.ClusterSize = 1u << (int)header.ClusterBits;

            return header;
        }

        private ulong[] ReadL1Table()
        {
            var table = new ulong[_header.L1Size];
            _baseStream.Position = (long)_header.L1TableOffset;

            var buffer = new byte[_header.L1Size * 8];
            _baseStream.ReadExactly(buffer, 0, buffer.Length);

            for (int i = 0; i < _header.L1Size; i++)
            {
                table[i] = ReadBigEndianUInt64(buffer, i * 8);
            }

            return table;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            if (offset + count > buffer.Length)
                throw new ArgumentException("Buffer too small");

            if (_position >= Length)
                return 0;

            int totalRead = 0;
            int remaining = (int)Math.Min(count, Length - _position);

            while (remaining > 0)
            {
                ulong virtualOffset = (ulong)_position;
                uint clusterOffset = (uint)(virtualOffset % _header.ClusterSize);
                int toRead = (int)Math.Min(remaining, _header.ClusterSize - clusterOffset);

                ulong? physicalOffset = TranslateAddress(virtualOffset);

                if (physicalOffset.HasValue)
                {
                    // Check if cluster is compressed
                    ulong l2Entry = GetL2Entry(virtualOffset);
                    if (IsCompressed(l2Entry))
                    {
                        ReadCompressedCluster(l2Entry, buffer, offset + totalRead, clusterOffset, toRead);
                    }
                    else
                    {
                        _baseStream.Position = (long)(physicalOffset.Value + clusterOffset);
                        _baseStream.ReadExactly(buffer, offset + totalRead, toRead);
                    }
                }
                else
                {
                    // Unallocated cluster - return zeros
                    Array.Clear(buffer, offset + totalRead, toRead);
                }

                totalRead += toRead;
                remaining -= toRead;
                _position += toRead;
            }

            return totalRead;
        }

        private ulong? TranslateAddress(ulong virtualOffset)
        {
            uint clusterBits = _header.ClusterBits;
            uint l2Bits = clusterBits - 3; // Each L2 entry is 8 bytes
            uint l1Index = (uint)(virtualOffset >> (int)(clusterBits + l2Bits));
            uint l2Index = (uint)((virtualOffset >> (int)clusterBits) & ((1u << (int)l2Bits) - 1));

            if (l1Index >= _l1Table.Length)
                return null;

            ulong l1Entry = _l1Table[l1Index];
            if ((l1Entry & 0x00FFFFFFFFFFFE00UL) == 0)
                return null; // L2 table not allocated

            ulong l2TableOffset = l1Entry & 0x00FFFFFFFFFFFE00UL;
            ulong l2Entry = ReadL2Entry(l2TableOffset, l2Index);

            if (IsCompressed(l2Entry))
            {
                return ExtractCompressedOffset(l2Entry);
            }

            ulong clusterOffset = l2Entry & 0x00FFFFFFFFFFFE00UL;
            if (clusterOffset == 0)
                return null; // Cluster not allocated

            return clusterOffset;
        }

        private ulong GetL2Entry(ulong virtualOffset)
        {
            uint clusterBits = _header.ClusterBits;
            uint l2Bits = clusterBits - 3;
            uint l1Index = (uint)(virtualOffset >> (int)(clusterBits + l2Bits));
            uint l2Index = (uint)((virtualOffset >> (int)clusterBits) & ((1u << (int)l2Bits) - 1));

            if (l1Index >= _l1Table.Length)
                return 0;

            ulong l1Entry = _l1Table[l1Index];
            if ((l1Entry & 0x00FFFFFFFFFFFE00UL) == 0)
                return 0;

            ulong l2TableOffset = l1Entry & 0x00FFFFFFFFFFFE00UL;
            return ReadL2Entry(l2TableOffset, l2Index);
        }

        private ulong ReadL2Entry(ulong l2TableOffset, uint l2Index)
        {
            _baseStream.Position = (long)(l2TableOffset + l2Index * 8);
            var buffer = new byte[8];
            _baseStream.ReadExactly(buffer, 0, 8);
            return ReadBigEndianUInt64(buffer, 0);
        }

        private bool IsCompressed(ulong l2Entry)
        {
            // Bit 62 indicates compression
            return (l2Entry & (1UL << 62)) != 0;
        }

        private ulong ExtractCompressedOffset(ulong l2Entry)
        {
            // For compressed clusters, bits 0-61 contain offset and size info
            int clusterBits = (int)_header.ClusterBits;
            int offsetBits = 62 - (clusterBits - 8);
            ulong offsetMask = (1UL << offsetBits) - 1;
            return (l2Entry & offsetMask) << (clusterBits - 8);
        }

        private void ReadCompressedCluster(ulong l2Entry, byte[] buffer, int offset, uint clusterOffset, int count)
        {
            int clusterBits = (int)_header.ClusterBits;
            int offsetBits = 62 - (clusterBits - 8);
            ulong offsetMask = (1UL << offsetBits) - 1;
            ulong sizeShift = (ulong)(clusterBits - 8);

            ulong hostOffset = (l2Entry & offsetMask) << (int)sizeShift;
            ulong compressedSize = ((l2Entry >> offsetBits) & ((1UL << (clusterBits - 8)) - 1)) + 1;
            compressedSize <<= 9; // Size is in 512-byte sectors

            _baseStream.Position = (long)hostOffset;

            // Read compressed data
            var compressedData = new byte[compressedSize];
            _baseStream.ReadExactly(compressedData, 0, (int)compressedSize);

            // Decompress using zlib (deflate with 2-byte header)
            using (var ms = new MemoryStream(compressedData, 2, (int)compressedSize - 2)) // Skip zlib header
            using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
            {
                var decompressed = new byte[_header.ClusterSize];
                int decompressedSize = deflate.Read(decompressed, 0, decompressed.Length);

                int bytesToCopy = Math.Min(count, decompressedSize - (int)clusterOffset);
                Array.Copy(decompressed, clusterOffset, buffer, offset, bytesToCopy);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0 || newPosition > Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = newPosition;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Helper methods for big-endian reading
        private static uint ReadBigEndianUInt32(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }

        private static ulong ReadBigEndianUInt64(byte[] buffer, int offset)
        {
            return ((ulong)buffer[offset] << 56) |
                   ((ulong)buffer[offset + 1] << 48) |
                   ((ulong)buffer[offset + 2] << 40) |
                   ((ulong)buffer[offset + 3] << 32) |
                   ((ulong)buffer[offset + 4] << 24) |
                   ((ulong)buffer[offset + 5] << 16) |
                   ((ulong)buffer[offset + 6] << 8) |
                   buffer[offset + 7];
        }

        private class Qcow2Header
        {
            public uint Magic { get; set; }
            public uint Version { get; set; }
            public ulong BackingFileOffset { get; set; }
            public uint BackingFileSize { get; set; }
            public uint ClusterBits { get; set; }
            public ulong Size { get; set; }
            public uint CryptMethod { get; set; }
            public uint L1Size { get; set; }
            public ulong L1TableOffset { get; set; }
            public ulong RefcountTableOffset { get; set; }
            public uint RefcountTableClusters { get; set; }
            public uint NbSnapshots { get; set; }
            public ulong SnapshotsOffset { get; set; }
            public uint ClusterSize { get; set; }
        }
    }
}
