using System.Buffers;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils.Ntfs;
using DiscUtils.Streams;

namespace auvdisk.Fs.Ntfs
{
    public static class NtfsClone
    {
        private class ProgressData : IProgressData
        {
            public long TotalBytes { get; init; } = 0;
            public int IncrementBytes { get; set; } = 0;
            public string Description => "Cloning...";
            public string Complete => "Cloned";
        }
        
        public static Flow<None> Clone(Stream source, Stream target, ILog logger)
        {
            try
            {
                logger.Log("Copying NTFS partition contents. Only used clusters will be copied.");

                using var ntfs = new NtfsFileSystem(source);

                ntfs.NtfsOptions.HideSystemFiles = false;
                ntfs.NtfsOptions.HideHiddenFiles = false;
                ntfs.NtfsOptions.HideMetafiles = false;

                byte[] volumeBitmap;

                using (var bitmapStream = ntfs.OpenFile(@"$Bitmap", FileMode.Open))
                {
                    volumeBitmap = bitmapStream.ReadExactly((int)bitmapStream.Length);
                }

                var clusterSize = (int)ntfs.ClusterSize;

                CopyBootSector(source, target, clusterSize);
                var ranges = BitmapToRanges(volumeBitmap, clusterSize);

                var progressData = new ProgressData
                {
                    TotalBytes = ranges.Select(x => x.Length).Aggregate(0L, (a, b) => a + b),
                };

                const int defaultCopyBufferSize = 81920;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(defaultCopyBufferSize);

                Utils.WithProgress(logger, progressData, progress =>
                {
                    try
                    {
                        foreach (var extent in ranges)
                        {
                            var sub = new SubStream(source, extent.Start, extent.Length);

                            int bytesRead;
                            target.Seek(extent.Start, SeekOrigin.Begin);

                            while ((bytesRead = sub.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                target.Write(buffer, 0, bytesRead);
                            }

                            progressData.IncrementBytes += (int)extent.Length;
                            progress?.Call(progressData);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    return progressData;
                });

                return Flows.Ok(None.Value, logger);
            }
            catch (Exception e)
            {
                return new($"Failed to clone NTFS volume with error: {e.Message}", logger);
            }
        }
        
        private static void CopyBootSector(Stream source, Stream target, long bytesPerCluster)
        {
            // Copy first 16 clusters (boot sector, $MFT start, and critical metadata)
            // This ensures all essential NTFS structures are copied
            int criticalClusters = 16;
            byte[] buffer = new byte[bytesPerCluster * criticalClusters];

            source.Position = 0;
            target.Position = 0;

            int bytesRead = source.Read(buffer, 0, buffer.Length);
            target.Write(buffer, 0, bytesRead);
        }
        
        private static bool IsBitSet(byte[] buffer, long bit)
        {
            var byteIdx = (int)(bit >> 3);
            if (byteIdx >= buffer.Length)
            {
                return false;
            }

            var val = buffer[byteIdx];
            var mask = (byte)(1 << (int)(bit & 0x7));

            return (val & mask) != 0;
        }

        private static IEnumerable<StreamExtent> BitmapToRanges(byte[] bitmap, int bytesPerCluster)
        {
            long numClusters = bitmap.Length * 8;
            long cluster = 0;
            while (cluster < numClusters && !IsBitSet(bitmap, cluster))
            {
                ++cluster;
            }

            while (cluster < numClusters)
            {
                var startCluster = cluster;
                while (cluster < numClusters && IsBitSet(bitmap, cluster))
                {
                    ++cluster;
                }

                yield return new StreamExtent(startCluster * bytesPerCluster, (cluster - startCluster) * bytesPerCluster);

                while (cluster < numClusters && !IsBitSet(bitmap, cluster))
                {
                    ++cluster;
                }
            }
        }
    }
}
