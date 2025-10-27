using System.Reflection.Metadata;
using auvdisk.Extensions;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Ntfs;
using DiscUtils.Streams;
using DiscUtils.Wim;
using DiskAccessLibrary.FileSystems.NTFS;
using DotNext;
using DiskAccessLibrary.VHD;

namespace auvdisk.DiskImage
{
    public class DiskProbe
    {
        public Log.ILog Logger { get; private set; }
        public Action<DiscFileSystem> FsHandler { get; private set; }
        public string Path { get; private set; }

        public record ProbeResult(DiskRecord? Disk, FileSystemRecord? Fs);
        public record DiskRecord(string PartitionTableType, List<PartitionRecord> Partitions, string ImageType, ulong SectorSize, Guid? PartTableGuid, Guid? DiskGuid);
        public record PartitionRecord(long StartLba, long EndLba, long SectorCountLba, Guid? PartGuid, Guid TypeGuid, Optional<FileSystemRecord> FileSystem);
        public record FileSystemRecord(string FsType, string VolumeLabel, long? Size, long? UsedSpace, long? AvailableSpace, long Offset, string? VolumeId);

        public DiskProbe(string path, Log.ILog logger, Action<DiscFileSystem>? fsHandler = null)
        {
            FsHandler = fsHandler ?? ListFsRoot;
            Logger = logger;
            Path = path;
        }

        public ProbeResult Probe()
        {
            Logger.Log("Starting disk probe");
            
            if (!File.Exists(Path))
            {
                Logger.Error($"Source file {Path} does not exist");

                return new ProbeResult(null, null);
            }

            // Checking for file access issues
            try
            {
                using var _ = new FileStream(Path, FileMode.Open, FileAccess.Read);
            }
            catch (IOException e)
            {
                Logger.Error($"{e.Message}");
                return new ProbeResult(null, null);
            }
            
            using var rawDisk = new DiscUtils.Raw.Disk(Path, FileAccess.Read);
            VHDFooter? maybeVhdFooter = Vhd.Util.ReadVhdFooterSafe(Path);

            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read);
            var wimFile = Utils.SuppressRef<Exception, WimFile>(() => new WimFile(stream));
            var qCowStream = Utils.SuppressRef<Exception, Bytes.Qcow2Stream>(() => new Bytes.Qcow2Stream(stream));

            using var duDetected = Utils.SuppressRef<Exception, VirtualDisk>(() => VirtualDisk.OpenDisk(Path, FileAccess.Read));

            if (qCowStream != null)
            {
                var qcowWrapped = new DiscUtils.Raw.Disk(qCowStream, Ownership.None);

                var diskRecord = HandleVirtualDisk(qcowWrapped, "qcow2", null);

                return new ProbeResult(diskRecord, null);
            }
            else if (wimFile != null)
            {
                Logger.Log("[yellow]WIM[/] file detected");
                List<PartitionRecord> parts = [];

                for (int imgIdx = 0; imgIdx < wimFile.ImageCount; ++imgIdx)
                {
                    var wimImage = wimFile.GetImage(imgIdx);
                    Logger.Log($"Found image with index [yellow]{imgIdx + 1}[/], label [yellow]{wimImage.VolumeLabel}[/]");
                    var fs = new FileSystemRecord("WIM", wimImage.VolumeLabel, null, null, null, 0,
                        wimImage.VolumeId.ToString());
                    var part = new PartitionRecord(0, 0, 0, Guid.Empty, Guid.Empty, fs);
                    parts.Add(part);
                    FsHandler(wimImage);
                }

                return new ProbeResult(new DiskRecord("WIM", parts, "WIM", 512, null, null), null);
            }
            else if (maybeVhdFooter?.IsValid ?? false)
            {
                Logger.Log($"Valid VHD footer with id [yellow]{maybeVhdFooter.UniqueId}[/] was found, assuming VHD file format");
                
                using var vhdDisk = Vhd.Util.OpenDiskWithDu(Path, Logger);
                //using var vhdDisk = new DiscUtils.Vhd.Disk(Path, FileAccess.Read);

                if (vhdDisk != null)
                {
                    return new ProbeResult(HandleVirtualDisk(vhdDisk, "VHD", maybeVhdFooter.UniqueId), null);
                }
                else  
                {
                    // Basically this means that we're unable to locate parent for differencing VHD
                    // Log will already have all the details
                    return new ProbeResult(null, null);
                }
            }
            else if (rawDisk is { IsPartitioned: true, Partitions: DiscUtils.Partitions.GuidPartitionTable} &&
                rawDisk.Partitions.Partitions.Count > 0)
            {
                Logger.Log($"Found sensible GPT partition table at offset [yellow]0[/], assuming RAW disk image");
                return new ProbeResult(HandleVirtualDisk(rawDisk, "RAW"), null);
            }
            else if (duDetected is DiscUtils.OpticalDisk.Disc)
            {
                Logger.Log("Utilizing DiscUtils heuristics to determine possible disk image type");
                Logger.Log("Processing file as [yellow]ISO[/] image");
                using var cd = new CDReader(duDetected.Content, true);
                return new ProbeResult(null, HandleFileSystem(cd.RawStream, 0, FsHandler, null));
            }
            else if (duDetected != null)
            {
                Logger.Log("Utilizing DiscUtils heuristics to determine possible disk image type");
                return new ProbeResult(HandleVirtualDisk(duDetected, GetDiskType(duDetected)), null);
            }
            else if (rawDisk is { IsPartitioned: true, Partitions: DiscUtils.Partitions.BiosPartitionTable } &&
                     rawDisk.Partitions.Partitions.Count > 0)
            {
                Logger.Log($"Found sensible MBR partition table at offset [yellow]0[/], assuming RAW disk image");
                return new ProbeResult(HandleVirtualDisk(rawDisk, "RAW"), null);
            }
            else
            {
                Logger.Warning(
                    "Failed to determine virtual disk format, trying to open file as raw filesystem dump file");
                using var fsStream = new FileStream(Path, FileMode.Open, FileAccess.Read);

                return new ProbeResult(null, HandleFileSystem(fsStream, 0, FsHandler));
            }
        }

        private string GetDiskType(DiscUtils.VirtualDisk disk)
        {
            var knownTypes = new Dictionary<Type, string>
            {
                {typeof(DiscUtils.Raw.Disk), "RAW"},
                {typeof(DiscUtils.Vhd.Disk), "VHD"},
                {typeof(DiscUtils.Vhdx.Disk), "VHDX"}
            };

            if (knownTypes.ContainsKey(disk.GetType()))
            {
                return knownTypes[disk.GetType()];
            }
            else
            {
                return disk.GetType().FullName ?? disk.GetType().Name;
            }
        }

        public void ListFsRoot(DiscFileSystem fs)
        {
            Logger.Log("Listing filesystem root contents:");

            foreach (var dir in fs.GetDirectories(""))
            {
                Logger.Log("d   /" + dir.FormatDuPath());
            }

            foreach (var f in fs.GetFiles(""))
            {
                Logger.Log("    /" + f.FormatDuPath());
            }
        }

        public static Action<DiscFileSystem> GetListArbitraryDir(string path, Log.ILog logger)
        {
            return (DiscFileSystem fs) =>
            {
                string prettyPath = "/" + path.FormatDuPath();
                string searchPath = path.FormatDuPath(false);

                logger.Log("Listing contents of " + prettyPath + ":");

                try
                {
                    foreach (var dir in fs.GetDirectories(searchPath))
                    {
                        logger.Log("d   /" + dir.FormatDuPath());
                    }

                    foreach (var f in fs.GetFiles(searchPath))
                    {
                        logger.Log("    /" + f.FormatDuPath());
                    }
                }
                catch (DirectoryNotFoundException ex)
                {
                    logger.Error(ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for FAT throws this
                {
                    logger.Error(ex.Message);
                }
                catch (Exception ex)
                {
                    logger.Error("Unexpected exception " + ex.GetType().ToString() + ": " + ex.Message);
                }
            };
        }

        public static Action<DiscFileSystem> GetCatArbitraryFile(string path, Log.ILog logger)
        {
            return (DiscFileSystem fs) =>
            {
                string prettyPath = "/" + path.FormatDuPath();
                string searchPath = path.FormatDuPath(false);

                try
                {
                    using (var stream = fs.OpenFile(searchPath, FileMode.Open))
                    {
                        logger.Log("File " + path + " contents:");

                        // Mostly to be able to intercept output in tests
                        // we still need to properly stream large files
                        if (stream.Length < 1024)
                        {
                            var streamReader = new StreamReader(stream);
                            
                            logger.Log(streamReader.ReadToEnd());
                        }
                        else
                        {
                            // TODO: support streaming in logger?
                            stream.CopyTo(Console.OpenStandardOutput());
                        }
                    }
                }
                catch (FileNotFoundException ex)
                {
                    logger.Error(ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for fat throws this
                {
                    logger.Error(ex.Message);
                }
                catch (Exception ex)
                {
                    logger.Error("Unexpected exception " + ex.GetType().ToString() + ": " + ex.Message);
                }
            };
        }

        private DiskRecord? HandleVirtualDisk(DiscUtils.VirtualDisk vdisk, string imageType, Guid? diskGuid = null)
        {
            Logger.Log($"Processing virtual disk of type [yellow]{imageType}[/] with LBA size [yellow]{vdisk.SectorSize}[/] bytes");

            if (vdisk.IsPartitioned)
            {
                var diskRecord = new DiskRecord( 
                    PartitionTableType: vdisk.Partitions.ToReadableString(),
                    Partitions: new List<PartitionRecord>(),
                    ImageType: imageType,
                    SectorSize: (ulong)vdisk.SectorSize,
                    PartTableGuid: vdisk.Partitions.DiskGuid,
                    DiskGuid: diskGuid
                );

                if (diskRecord.PartTableGuid != null && diskRecord.PartTableGuid != Guid.Empty)
                {
                    Logger.Log($"[green]Found partition table of type [yellow]{diskRecord.PartitionTableType}[/] with id [yellow]{diskRecord.PartTableGuid}[/][/]");
                }
                else
                {
                    Logger.Log($"[green]Found partition table of type [yellow]{diskRecord.PartitionTableType}[/][/]");
                }
                
                var volumeManager = new VolumeManager(vdisk);
                
                foreach (var (volume, idx) in volumeManager.GetPhysicalVolumes().Select((volume, idx) => (volume, idx)))
                {
                    var partition = volume.Partition;

                    if (partition.GuidType != Guid.Empty)
                    {
                        Logger.Log(
                            $"[green]Found partition [yellow]{idx}[/] starting at [yellow]{partition.FirstSector}[/] LBA, ending at [yellow]{partition.LastSector}[/] LBA of type [yellow]{partition.GuidType}[/][/]");
                    }
                    else
                    {
                        Logger.Log($"[green]Found partition [yellow]{idx}[/] starting at [yellow]{partition.FirstSector}[/] LBA, ending at [yellow]{partition.LastSector}[/][/]");
                    }

                    Guid? partGuid = null;
                    
                    if (partition is DiscUtils.Partitions.GuidPartitionInfo)
                    {
                        partGuid = (partition as DiscUtils.Partitions.GuidPartitionInfo)!.Identity;
                        Logger.Log($"[green]Partition ID: [yellow]{partGuid.ToString()}[/][/]");
                    }
                    
                    var maybeFsRecord = HandleFileSystem(partition.Open(), (ulong)partition.FirstSector * (ulong)vdisk.SectorSize, FsHandler, volume);

                    var partitionRecord = new PartitionRecord(
                        StartLba: partition.FirstSector,
                        EndLba: partition.LastSector,
                        PartGuid: partGuid,
                        TypeGuid: partition.GuidType,
                        SectorCountLba: partition.SectorCount,
                        FileSystem: maybeFsRecord
                    );

                    diskRecord.Partitions.Add(partitionRecord);
                }

                return diskRecord;
            }

            return null;
        }
        
        private FileSystemRecord? HandleFileSystem(Stream stream, ulong offset, Action<DiscFileSystem> impl, VolumeInfo? volumeInfo = null)
        {
            FileSystemRecord FillFsRecord(DiscUtils.FileSystemInfo fsInfo, DiscFileSystem fs, string? volumeId)
            {
                return new FileSystemRecord(
                    FsType: fsInfo.Name.ToUpper(), 
                    VolumeLabel: fs.VolumeLabel, 
                    Size: Utils.SuppressVal<NotSupportedException, long>(() => fs.Size),
                    AvailableSpace: Utils.SuppressVal<NotSupportedException, long>(() => fs.AvailableSpace),
                    UsedSpace: Utils.SuppressVal<NotSupportedException, long>(() => fs.UsedSpace),
                    VolumeId: volumeId, 
                    Offset: (long)offset);
            }

            void OutputVolumeInfo(FileSystemRecord record)
            {
                Logger.Log($"[green]Found filesystem of type [yellow]{record.FsType}[/] at [yellow]{record.Offset}[/] bytes with length of [yellow]{stream.Length}[/] bytes[/]");

                if (record.VolumeId != null)
                {
                    Logger.Log($"[green]Volume ID: [yellow]{record.VolumeId}[/][/]");
                }
            }
            
            var fsList = volumeInfo != null
                ? FileSystemManager.DetectFileSystems(volumeInfo)
                : FileSystemManager.DetectFileSystems(stream);
            
            if (fsList.Count > 0)
            {
                var fsRecords = new List<FileSystemRecord>();

                // TODO: modify record to support multiple filesystems per volume?
                foreach (var fsInfo in fsList)
                {
                    try
                    {
                        using var fs = fsInfo.Open(stream);
                        var record = FillFsRecord(fsInfo, fs, Fs.Util.ExtractUuid(fs, Logger));
                        OutputVolumeInfo(record);
                        impl(fs);

                        fsRecords.Add(record);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"Failed to read {fsInfo.Name}: {e.Message}");
                    }
                }

                return fsRecords.Count == 0 ? null : fsRecords.First();
            }

            return null;
        }
    }
}
