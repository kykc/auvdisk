using auvdisk.Extensions;
using DiscUtils;
using DotNext;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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

    internal class DiskProbe
    {
        public Action<string> Logger { get; private set; }
        public Action<DiscFileSystem> FsHandler { get; private set; }
        public string Path { get; private set; }
        public long Offset { get; private set; }
        public long Trim { get; private set; }

        public record ProbeResult(DiskRecord? Disk, FileSystemRecord? Fs);
        public record DiskRecord(string PartitionTableType, List<PartitionRecord> Partitions, string ImageType);
        public record PartitionRecord(long StartLba, long EndLba, long SectorCountLba, Guid? PartGuid, Guid TypeGuid, Optional<FileSystemRecord> FileSystem);
        public record FileSystemRecord(string FsType, string VolumeLabel, long Size, long UsedSpace, long AvailableSpace);

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
            // TODO: file exists
            var rawFileStream = new FileStream(Path, FileMode.Open);

            using (var fileStream = new OffsetStreamDecorator(rawFileStream, Offset, Trim))
            {
                var openVhd = () => new DiscUtils.Vhd.Disk(fileStream, DiscUtils.Streams.Ownership.None);

                var openRaw = () =>
                {
                    var vdisk = new DiscUtils.Raw.Disk(fileStream, DiscUtils.Streams.Ownership.None);

                    if (!vdisk.IsPartitioned)
                    {
                        throw new InvalidDataException("Unable to detect partition table in RAW image");
                    }

                    return vdisk;
                };

                if (openVhd.ActWithLog(Logger, "Trying to open file as VHD image") is var resultVhd && resultVhd.IsSuccessful)
                {
                    var diskRecord = HandleVirtualDisk(resultVhd.Value, "VHD");

                    return new ProbeResult(Disk: diskRecord, Fs: null);
                }
                else if (openRaw.ActWithLog(Logger, "Trying to open file as RAW image") is var resultRaw && resultRaw.IsSuccessful)
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
                    logger("Error: " + ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for FAT throws this
                {
                    logger("Error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    logger("Unexpected Error " + ex.GetType().ToString() + ":" + ex.Message);
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
                        stream.CopyTo(Console.OpenStandardOutput());
                    }
                }
                catch (FileNotFoundException ex)
                {
                    logger("Error: " + ex.Message);
                }
                catch (ArgumentException ex) // Filename too long for fat throws this
                {
                    logger("Error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    logger("Unexpected Error " + ex.GetType().ToString() + ":" + ex.Message);
                }
            };
        }

        private DiskRecord? HandleVirtualDisk(DiscUtils.VirtualDisk vdisk, string imageType)
        {
            if (vdisk.IsPartitioned)
            {
                Logger("Found partition table " + vdisk.Partitions.GetType());

                var diskRecord = new DiskRecord( 
                    PartitionTableType: vdisk.Partitions is DiscUtils.Partitions.GuidPartitionTable ? "GPT" : "MBR", // TODO: more broad PT type detection
                    Partitions: new List<PartitionRecord>(),
                    ImageType: imageType
                );

                foreach (var partition in vdisk.Partitions.Partitions)
                {
                    Logger("Found partition starting at " + partition.FirstSector + " LBA, ending at " + partition.LastSector + " LBA of type " + partition.GuidType);

                    Guid? partGuid = null;
                    
                    if (partition is DiscUtils.Partitions.GuidPartitionInfo)
                    {
                        partGuid = (partition as DiscUtils.Partitions.GuidPartitionInfo)!.Identity;
                    }

                    var maybeFsRecord = HandleFileSystem(partition.Open(), (ulong)partition.FirstSector * Program.LbaSize, FsHandler);

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

        private string GetFsType(DiscFileSystem fs)
        {
            if (fs is DiscUtils.Ext.ExtFileSystem)
            {
                return "EXT";
            }
            else if (fs is DiscUtils.Fat.FatFileSystem)
            {
                return "FAT32";
            }
            else if (fs is DiscUtils.Ntfs.NtfsFileSystem)
            {
                return "NTFS";
            }
            else
            {
                return "UNKNOWN";
            }
        }

#pragma warning disable CA1416
        private FileSystemRecord? HandleFileSystem(Stream stream, ulong offset, Action<DiscFileSystem> impl)
        {
            var openFat = () => new DiscUtils.Fat.FatFileSystem(stream, DiscUtils.Streams.Ownership.None);
            var openNtfs = () => new DiscUtils.Ntfs.NtfsFileSystem(stream); // TODO: exclude on non-Win platform
            var openExt = () => new DiscUtils.Ext.ExtFileSystem(stream);

            var fillFsRecord = (DiscFileSystem fs) =>
            {
                return new FileSystemRecord(
                    FsType: GetFsType(fs),
                    VolumeLabel: fs.VolumeLabel,
                    Size: fs.Size,
                    AvailableSpace: fs.AvailableSpace,
                    UsedSpace: fs.UsedSpace
                );
            };

            if (openNtfs.ActWithLog(Logger, "Trying to open as NTFS filesystem") is var resultNtfs && resultNtfs.IsSuccessful)
            {
                Logger("Found filesystem of type NTFS at " + offset.ToString() + " bytes");
                impl(resultNtfs.Value);
                return fillFsRecord(resultNtfs.Value);
            }
            else if (openFat.ActWithLog(Logger, "Trying to open as FAT filesystem") is var resultFat && resultFat.IsSuccessful)
            {
                Logger("Found filesystem of type FAT at " + offset.ToString() + " bytes");
                impl(resultFat.Value);
                return fillFsRecord(resultFat.Value);
            }
            else if (openExt.ActWithLog(Logger, "Trying to open as EXT filesystem") is var resultExt && resultExt.IsSuccessful)
            {
                Logger("Found filesystem of type EXT at " + offset.ToString() + " bytes");
                impl(resultExt.Value);
                return fillFsRecord(resultExt.Value);
            }

            return null;
#pragma warning restore CA1416
        }
    }
}
