using auvdisk.Extensions;
using DiscUtils;
using DiscUtils.Ntfs;
using DiskAccessLibrary.FileSystems.NTFS;
using DotNext;
using DiskAccessLibrary.VHD;

namespace auvdisk
{
    public class DiskProbe
    {
        public Action<string> Logger { get; private set; }
        public Action<DiscFileSystem> FsHandler { get; private set; }
        public string Path { get; private set; }

        public record ProbeResult(DiskRecord? Disk, FileSystemRecord? Fs);
        public record DiskRecord(string PartitionTableType, List<PartitionRecord> Partitions, string ImageType, ulong SectorSize, Guid? PartTableGuid, Guid? DiskGuid);
        public record PartitionRecord(long StartLba, long EndLba, long SectorCountLba, Guid? PartGuid, Guid TypeGuid, Optional<FileSystemRecord> FileSystem);
        public record FileSystemRecord(string FsType, string VolumeLabel, long? Size, long? UsedSpace, long? AvailableSpace, long Offset, string? VolumeId);

        public DiskProbe(string path, Action<DiscFileSystem>? fsHandler = null, Action<string>? logger = null)
        {
            FsHandler = fsHandler ?? ListFsRoot;
            Logger = logger ?? ((string s) => Console.WriteLine(s));
            Path = path;
        }

        public ProbeResult Probe()
        {
            Logger("Starting disk probe");
            
            if (!File.Exists(Path))
            {
                Logger($"ERROR: source file {Path} does not exist");

                return new ProbeResult(null, null);
            }

            // Checking for file access issues
            try
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read);
            }
            catch (IOException e)
            {
                Logger($"ERROR: {e.Message}");
                return new ProbeResult(null, null);
            }
            
            using var rawDisk = new DiscUtils.Raw.Disk(Path, FileAccess.Read);
            VHDFooter? maybeVhdFooter = Vhd.Util.ReadVhdFooterSafe(Path);

            if (maybeVhdFooter?.IsValid ?? false)
            {
                Logger($"Valid VHD footer with id {maybeVhdFooter.UniqueId} was found, assuming VHD file format");
                using var vhdDisk = new DiscUtils.Vhd.Disk(Path, FileAccess.Read);

                return new ProbeResult(HandleVirtualDisk(vhdDisk, "VHD", maybeVhdFooter.UniqueId), null);
            }
            else if (rawDisk is { IsPartitioned: true, Partitions: DiscUtils.Partitions.GuidPartitionTable} &&
                rawDisk.Partitions.Partitions.Count > 0)
            {
                Logger($"Found sensible GPT partition table at offset 0, assuming RAW disk image");
                return new ProbeResult(HandleVirtualDisk(rawDisk, "RAW"), null);
            }
            else if (Extensions.Extensions.SuppressRef<Exception, DiscUtils.VirtualDisk>(() => VirtualDisk.OpenDisk(Path, FileAccess.Read))
                     is { } detectedDisk)
            {
                Logger("Utilizing DiscUtils heuristics to determine possible disk image type");
                return new ProbeResult(HandleVirtualDisk(detectedDisk, GetDiskType(detectedDisk)), null);
            }
            else if (rawDisk is { IsPartitioned: true, Partitions: DiscUtils.Partitions.BiosPartitionTable } &&
                     rawDisk.Partitions.Partitions.Count > 0)
            {
                Logger($"Found sensible MBR partition table at offset 0, assuming RAW disk image");
                return new ProbeResult(HandleVirtualDisk(rawDisk, "RAW"), null);
            }
            else
            {
                Logger(
                    "WARNING: failed to determine virtual disk format, trying to open file as raw filesystem dump file");
                using var fsStream = new FileStream(Path, FileMode.Open, FileAccess.Read);

                return new ProbeResult(null, HandleFileSystem(fsStream, 0, FsHandler));
            }
        }

        private string GetDiskType(DiscUtils.VirtualDisk disk)
        {
            var knownTypes = new Dictionary<Type, string>
            {
                {typeof(DiscUtils.Raw.Disk), "RAW"},
                {typeof(DiscUtils.Vhd.Disk), "VHD"} 
            };

            if (knownTypes.ContainsKey(disk.GetType()))
            {
                return knownTypes[disk.GetType()];
            }
            else
            {
                return disk.GetType().Name;
            }
        }

        public void ListFsRoot(DiscFileSystem fs)
        {
            Logger("Listing filesystem root contents:");

            foreach (var dir in fs.GetDirectories(""))
            {
                Logger("d   /" + dir.FormatDuPath());
            }

            foreach (var f in fs.GetFiles(""))
            {
                Logger("    /" + f.FormatDuPath());
            }
        }

        public static Action<DiscFileSystem> GetListArbitraryDir(string path, Action<string> logger)
        {
            return (DiscFileSystem fs) =>
            {
                string prettyPath = "/" + path.FormatDuPath();
                string searchPath = path.FormatDuPath(false);

                logger("Listing contents of " + prettyPath + ":");

                try
                {
                    foreach (var dir in fs.GetDirectories(searchPath))
                    {
                        logger("d   /" + dir.FormatDuPath());
                    }

                    foreach (var f in fs.GetFiles(searchPath))
                    {
                        logger("    /" + f.FormatDuPath());
                    }
                }
                catch (DirectoryNotFoundException ex)
                {
                    logger("ERROR: " + ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for FAT throws this
                {
                    logger("ERROR: " + ex.Message);
                }
                catch (Exception ex)
                {
                    logger("Unexpected Error " + ex.GetType().ToString() + ": " + ex.Message);
                }
            };
        }

        public static Action<DiscFileSystem> GetCatArbitraryFile(string path, Action<string> logger)
        {
            return (DiscFileSystem fs) =>
            {
                string prettyPath = "/" + path.FormatDuPath();
                string searchPath = path.FormatDuPath(false);

                try
                {
                    using (var stream = fs.OpenFile(searchPath, FileMode.Open))
                    {
                        logger("File " + path + " contents:");

                        // Mostly to be able to intercept output in tests
                        // we still need to properly stream large files
                        if (stream.Length < 1024)
                        {
                            var streamReader = new StreamReader(stream);
                            
                            logger(streamReader.ReadToEnd());
                        }
                        else
                        {
                            stream.CopyTo(Console.OpenStandardOutput());
                        }
                    }
                }
                catch (FileNotFoundException ex)
                {
                    logger("ERROR: " + ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for fat throws this
                {
                    logger("ERROR: " + ex.Message);
                }
                catch (Exception ex)
                {
                    logger("Unexpected Error " + ex.GetType().ToString() + ": " + ex.Message);
                }
            };
        }

        private DiskRecord? HandleVirtualDisk(DiscUtils.VirtualDisk vdisk, string imageType, Guid? diskGuid = null)
        {
            Logger($"Processing virtual disk of type {imageType} with LBA size {vdisk.SectorSize} bytes");
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
                    Logger($"[green]Found partition table of type {diskRecord.PartitionTableType} with id {diskRecord.PartTableGuid}[/]");
                }
                else
                {
                    Logger($"[green]Found partition table of type {diskRecord.PartitionTableType}[/]");
                }
                
                var volumeManager = new VolumeManager(vdisk);
                
                foreach (var (volume, idx) in volumeManager.GetPhysicalVolumes().Select((volume, idx) => (volume, idx)))
                {
                    var partition = volume.Partition;

                    if (partition.GuidType != Guid.Empty)
                    {
                        Logger(
                            $"[green]Found partition {idx} starting at {partition.FirstSector} LBA, ending at {partition.LastSector} LBA of type {partition.GuidType}[/]");
                    }
                    else
                    {
                        Logger($"[green]Found partition {idx} starting at {partition.FirstSector} LBA, ending at {partition.LastSector}[/]");
                    }

                    Guid? partGuid = null;
                    
                    if (partition is DiscUtils.Partitions.GuidPartitionInfo)
                    {
                        partGuid = (partition as DiscUtils.Partitions.GuidPartitionInfo)!.Identity;
                        Logger($"[green]Partition ID: {partGuid.ToString()}[/]");
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
                    Size: Extensions.Extensions.SuppressVal<NotSupportedException, long>(() => fs.Size),
                    AvailableSpace: Extensions.Extensions.SuppressVal<NotSupportedException, long>(() => fs.AvailableSpace),
                    UsedSpace: Extensions.Extensions.SuppressVal<NotSupportedException, long>(() => fs.UsedSpace),
                    VolumeId: volumeId, 
                    Offset: (long)offset);
            }

            void OutputVolumeInfo(FileSystemRecord record)
            {
                Logger($"[green]Found filesystem of type {record.FsType} at {record.Offset} bytes with length of {stream.Length} bytes[/]");

                if (record.VolumeId != null)
                {
                    Logger($"[green]Volume ID: {record.VolumeId}[/]");
                }
            }
            
            var fsList = volumeInfo != null
                ? FileSystemManager.DetectFileSystems(volumeInfo)
                : FileSystemManager.DetectFileSystems(stream);
            
            if (fsList.Count > 0)
            {
                return fsList.Select((fsInfo) =>
                {
                    using var fs = fsInfo.Open(stream);
                    var record = FillFsRecord(fsInfo, fs, FsUtils.ExtractUuid(fs, Logger));
                    OutputVolumeInfo(record);
                    impl(fs);

                    return record;
                }).First(); // TODO: modify record to support multiple filesystems per volume?
            }

            return null;
        }
    }
}
