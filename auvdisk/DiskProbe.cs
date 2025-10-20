using auvdisk.Extensions;
using DiscUtils;
using DotNext;
using DiskAccessLibrary.VHD;

namespace auvdisk
{
    // prefer discutils if there are no issues as there are more stuff implemented there
    // disc | try to open as vhd, if success to partition table
    // disc | try to open as raw, if success to partition table
    // PT   | try to open as GPT, if success list partitions and to FS
    // PT   | try to open as MBR, if success list partitions and to FS
    // FS   | try to open as NTFS, if success list root
    // FS   | try to open as FAT32, if success list root
    // FS   | try to open as EXT4, if success list root

    public class DiskProbe
    {
        public Action<string> Logger { get; private set; }
        public Action<DiscFileSystem> FsHandler { get; private set; }
        public string Path { get; private set; }
        public long Offset { get; private set; }
        public long Trim { get; private set; }

        public record ProbeResult(DiskRecord? Disk, FileSystemRecord? Fs);
        public record DiskRecord(string PartitionTableType, List<PartitionRecord> Partitions, string ImageType);
        public record PartitionRecord(long StartLba, long EndLba, long SectorCountLba, Guid? PartGuid, Guid TypeGuid, Optional<FileSystemRecord> FileSystem);
        public record FileSystemRecord(string FsType, string VolumeLabel, long? Size, long? UsedSpace, long? AvailableSpace, long Offset, string? VolumeId);

        public DiskProbe(string path, long offset, long trim, Action<DiscFileSystem>? fsHandler = null, Action<string>? logger = null)
        {
            FsHandler = fsHandler == null ? ListFsRoot : fsHandler;
            Logger = logger == null ? (string s) => Console.WriteLine(s) : logger;
            Path = path;
            Offset = offset;
            Trim = trim;
        }

        public ProbeResult Probe()
        {
            Logger("Starting disk probe");
            
            if (!File.Exists(Path))
            {
                Logger($"ERROR: source file {Path} does not exist");

                return new ProbeResult(null, null);
            }

            try
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read);
            }
            catch (IOException e)
            {
                Logger($"ERROR: {e.Message}");
                return new ProbeResult(null, null);
            }
            
            if (Vhd.Util.ReadVhdFooterSafe(Path) is { DiskType: VirtualHardDiskType.Differencing, IsValid: true })
            {
                return ProbeInVhdFileMode();
            }
            else
            {
                var streamModeResult = ProbeInStreamMode();

                var foundMeaningfulPartitions = streamModeResult.Disk?.Partitions?.Any(p =>
                    p.FileSystem is { HasValue: true, Value.FsType.Length: > 0 }) ?? false;

                if (streamModeResult.Fs != null || foundMeaningfulPartitions)
                {
                    return streamModeResult;
                }
                
                Logger($"Didn't find meaningful partitions while trying to open file as VHD/RAW image, falling back to DiscUtil heuristics");
                Logger($"WARNING: offset and trim are ignored in this mode due to DiscUtils API limitations");
                using var disk = VirtualDisk.OpenDisk(Path, FileAccess.Read);

                return disk != null
                    ? new ProbeResult(HandleVirtualDisk(disk, disk.GetType().ToString()), null) // TODO: human readable disk type
                    : new ProbeResult(null, null);
            }
        }

        private ProbeResult ProbeInVhdFileMode()
        {
            Logger($"Trying to open file as differencing VHD image");

            if (Offset != 0 || Trim != 0)
            {
                Logger($"WARNING: offset and trim are ignored in this mode");
            }
            
            using var vdisk = new DiscUtils.Vhd.Disk(Path, FileAccess.Read);
            var vdiskRecord = HandleVirtualDisk(vdisk, "VHD");

            return new ProbeResult(Disk: vdiskRecord, Fs: null);
        }

        private ProbeResult ProbeInStreamMode()
        {
            var rawFileStream = new FileStream(Path, FileMode.Open, FileAccess.Read);

            using (var fileStream = new OffsetStreamDecorator(rawFileStream, Offset, Trim))
            {
                var openVhd = () =>
                {
                    var vdisk = new DiscUtils.Vhd.Disk(fileStream, DiscUtils.Streams.Ownership.None);

                    // Differencing VHDs cannot be opened with FileStream in DiscUtils, so those are the only two options
                    Logger($"VHD type is {(vdisk.Layers.First().IsSparse ? "Dynamic" : "Fixed")}");
                    
                    return vdisk;
                };

                var openRaw = () =>
                {
                    var vdisk = new DiscUtils.Raw.Disk(fileStream, DiscUtils.Streams.Ownership.None);

                    if (!vdisk.IsPartitioned)
                    {
                        throw new InvalidDataException("Unable to detect partition table in RAW image");
                    }
                    else if (vdisk.Partitions.Partitions.Count == 0)
                    {
                        throw new InvalidDataException("Partition table is empty, will try to open as FS");
                    }

                    return vdisk;
                };

                if (openVhd.ActWithLog(Logger, "Trying to open file as VHD image", "WARNING") is var resultVhd && resultVhd.IsSuccessful)
                {
                    var diskRecord = HandleVirtualDisk(resultVhd.Value, "VHD");

                    return new ProbeResult(Disk: diskRecord, Fs: null);
                }
                else if (openRaw.ActWithLog(Logger, "Trying to open file as RAW image", "WARNING") is var resultRaw && resultRaw.IsSuccessful)
                {
                    var diskRecord = HandleVirtualDisk(resultRaw.Value, "RAW");

                    return new ProbeResult(Disk: diskRecord, Fs: null);
                }
                else
                {
                    var fsRecord = HandleFileSystem(fileStream, 0, FsHandler);

                    return new ProbeResult(Disk: null, Fs: fsRecord);
                }
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

        private DiskRecord? HandleVirtualDisk(DiscUtils.VirtualDisk vdisk, string imageType)
        {
            if (vdisk.IsPartitioned)
            {
                var diskRecord = new DiskRecord( 
                    PartitionTableType: vdisk.Partitions.ToReadableString(),
                    Partitions: new List<PartitionRecord>(),
                    ImageType: imageType
                );
                
                Logger($"[green]Found partition table of type {diskRecord.PartitionTableType}[/]");
                
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
                    
                    var maybeFsRecord = HandleFileSystem(partition.Open(), (ulong)partition.FirstSector * Program.LbaSize, FsHandler, volume);

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

#pragma warning disable CA1416
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
                Logger($"[green]Found filesystem of type {record.FsType} at {record.Offset} bytes[/]");

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
#pragma warning restore CA1416
    }
}
