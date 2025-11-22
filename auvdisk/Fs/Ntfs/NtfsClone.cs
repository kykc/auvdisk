using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils.Ntfs;
using DiscUtils.Streams;
using DotNext.Collections.Generic;

namespace auvdisk.Fs.Ntfs
{
    public static class NtfsClone
    {
        private class ProgressData : IProgressData
        {
            public long TotalBytes { get; init; } = 0;
            public long IncrementBytes { get; set; } = 0;
            public long ExtentBytes { get; set; } = 0;
            public string Description => "Cloning...";
            public string Complete => "Cloned";
        }

        public static void TestLastNtfsCluster(Stream source, ILog logger)
        {
            string CalcChecksum(byte[] subj)
            {
                var hasher = SHA256.Create();
                var checksum = hasher.ComputeHash(new MemoryStream(subj));
                var checksumString = BitConverter.ToString(checksum).Replace("-", String.Empty);
                return checksumString;
            }

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
            logger.Log($"Cluster size: {clusterSize}");
            logger.Log($"Volume capacity: {source.Length}");
            var ranges = BitmapToRanges(volumeBitmap, clusterSize);

            var lastExtent = ranges.Last();
            
            var lastExtentBytes = new byte[lastExtent.Length];
            
            source.Position = lastExtent.Start;
            int bytesRead = source.Read(lastExtentBytes, 0, lastExtentBytes.Length);

            bool result = bytesRead == lastExtentBytes.Length;
            logger.Log($"Extent: {lastExtent}");
            logger.Log($"Bytes read: {bytesRead}");
            logger.Log($"Extent length: {lastExtentBytes.Length}");
            logger.Log($"Result: {(result ? "[green]success[/]" : "[red]failure[/]")}");
            
            var lastClusterBytes = ReconstructLastCluster(source, clusterSize, ntfs.SectorSize, logger);
            Console.WriteLine($"Checksum reconstructed: {CalcChecksum(lastClusterBytes)}");
            
            if (result)
            {
                Console.WriteLine($"  Checksum last extent: {CalcChecksum(lastExtentBytes)}");
            }
        }
        
        /*
         * The "official" status of the last cluster of the NTFS partition (or volume?) is very weird: it is always marked as
         * allocated in BAT, but by default Win32 APIs would deny you to read it. In practise it is some rudiment of the old times,
         * and really only the last sector of this cluster contains data, and this data is nothing more than a backup copy of the first
         * "boot" sector of the volume. Using this knowledge we can simply reconstruct this cluster contents if needed using first sector
         * of the same volume. Another way around it is to try and grant yourself FSCTL_ALLOW_EXTENDED_DASD_IO on a particular handle,
         * this way said cluster should become readable.
         * Sources: https://web.archive.org/web/20251121234507/https://community.osr.com/t/last-sector-of-partition-created-for-ntfs-volume/53303
         */
        private static byte[] ReconstructLastCluster(Stream source, int clusterSize, int sectorSize, ILog logger)
        {
            var position = source.Position;
            source.Position = 0;
            
            var lastClusterBytes = new byte[clusterSize];
            var bootSector = new byte[sectorSize];
            source.ReadExactly(bootSector);
            Array.Copy(bootSector, 0, lastClusterBytes, clusterSize - sectorSize, sectorSize);
            
            source.Position = position;
            
            return lastClusterBytes;
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
                var sectorSize = ntfs.SectorSize;
                logger.Log($"Cluster size: {clusterSize}");
                logger.Log($"Sector size: {sectorSize}");
                logger.Log($"Volume capacity: {source.Length}");
                CopyBootSector(source, target, clusterSize);

                logger.Log("Calculating total amount of bytes to be copied...");
                var ranges = BitmapToRanges(volumeBitmap, clusterSize).ToList();
                var progressData = new ProgressData
                {
                    TotalBytes = ranges.Select(x => x.Length).Aggregate(0L, (a, b) => a + b),
                };
                logger.Log($"Total bytes: {progressData.TotalBytes} ({progressData.TotalBytes.HumanizeBytes()})");

                const int defaultCopyBufferSize = 81920;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(defaultCopyBufferSize);
                List<string> warnings = new();
                
                var isLastExtent = (int idx) => idx == ranges.Count - 1;

                Utils.WithProgress(logger, progressData, progress =>
                {
                    try
                    {
                        foreach (var (extent, idx) in ranges.Select((x, idx) =>  (x, idx)))
                        {
                            var sub = new SubStream(source, extent.Start, extent.Length);
                            progressData.ExtentBytes = 0;
                            int bytesRead;
                            target.Seek(extent.Start, SeekOrigin.Begin);

                            while ((bytesRead = sub.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                target.Write(buffer, 0, bytesRead);
                                progressData.IncrementBytes += bytesRead;
                                progressData.ExtentBytes += bytesRead;
                                progress?.Call(progressData);
                            }

                            if (progressData.ExtentBytes != extent.Length && isLastExtent(idx))
                            {
                                warnings.Add(
                                    "Failed to read last cluster of the volume. This may happen for various reasons, more info here: " + 
                                    "https://web.archive.org/web/20251121234507/https://community.osr.com/t/last-sector-of-partition-created-for-ntfs-volume/53303");
                                warnings.Add("Reconstructing, as this cluster only contains the copy of the first sector of the volume, prepended by zeros.");
                                
                                var lastCluster = ReconstructLastCluster(source, clusterSize, sectorSize, logger);
                                target.Write(lastCluster, 0, lastCluster.Length);
                                progressData.IncrementBytes += lastCluster.Length;
                            }
                            else if (progressData.ExtentBytes != extent.Length)
                            {
                                var warning =
                                    $"Failed to copy extent in full. Start: {extent.Start}, length:{extent.Length}, written:{progressData.ExtentBytes}, diff:{extent.Length - progressData.ExtentBytes}";
                                warnings.Add(warning);
                                // We report this, so "fix up" the progress not to hang at 99%
                                // Otherwise this might lead to an impression that action was interrupted, while in reality it wasn't.
                                // One might say that we indeed processed those bytes by failing to copy them...
                                progressData.IncrementBytes += (extent.Length - progressData.ExtentBytes);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    return progressData;
                });

                foreach (var warning in warnings)
                {
                    logger.Warning(warning);
                }
                
                return Flows.Val(None.Value);
            }
            catch (Exception e) when (Program.ExceptionFilter(e))
            {
                return new($"Failed to clone NTFS volume with error: {e.Message}");
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
