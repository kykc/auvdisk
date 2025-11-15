using System.Collections.ObjectModel;
using auvdisk.Extensions;
using auvdisk.Fs;
using auvdisk.Log;
using DiscUtils;
using DiscUtils.Streams;
using DiscUtils.Wim;
using Spectre.Console;

namespace auvdisk.DiskImage
{
    public class FsCollection : IDisposable
    {
        public string Caption { get; }
        public IEnumerable<IDisposable> Entities { get; }
        public Dictionary<string, DiscFileSystem> FileSystems { get; }

        public FsCollection(IEnumerable<IDisposable> entities, Dictionary<string, DiscFileSystem> fileSystems, string caption)
        {
            Entities = entities;
            FileSystems = fileSystems;
            Caption = caption;
        }

        public void Dispose()
        {
            foreach (var entity in Entities)
            {
                entity.Dispose();
            }
        }
    }

    public static class Factory
    {
        private static Dictionary<string, DiscFileSystem> GetFileSystems(DiscUtils.VirtualDisk disk)
        {
            var volumeManager = new VolumeManager(disk);
            var fsList = volumeManager.GetPhysicalVolumes()
                .Select(volume => (volume, fsInfoList: FileSystemManager.DetectFileSystems(volume.Open())))
                .Select(x => x.fsInfoList.Select(fsInfo => fsInfo.Open(x.volume)))
                .SelectMany(x => x).ToList();

            return GetFileSystems(fsList);
        }

        internal static Dictionary<string, DiscFileSystem> GetFileSystems(IEnumerable<DiscFileSystem> fsList)
        {
            string GetFsName(int idx, DiscFileSystem fs)
            {
                var result = $"{idx:D2} " + fs.FriendlyName ?? fs.GetType().Name;

                if (Fs.Util.ExtractUuid(fs, new NullLogger()) is { } uuid)
                {
                    result += $" {uuid}";
                }
                else if (fs.VolumeLabel != String.Empty)
                {
                    result += $" {fs.VolumeLabel}";
                }
                else if (fs.VolumeId > 0)
                {
                    result += $" {fs.VolumeId}";
                }

                return result;
            }

            return fsList.Select((x, idx) => new KeyValuePair<string, DiscFileSystem>(GetFsName(idx, x), x)).ToDictionary();
        }

        public static Flow<FsCollection> MakeFsListFromAvailableVolumes(ILog logger, bool treeOutput = false)
        {
            return Interop.Common.GetVolumes(logger).Map((volumeList) =>
            {
                var fsList = new List<DiscFileSystem>();
                var disposableList = new List<IDisposable>();

                Dictionary<string, List<string[]>> tuiStructure = new();
                
                foreach (var (volume, idx) in volumeList.OrderBy((v) => v.DeviceId, Interop.Common.GetDeviceIdComparer()).Select((x, idx) => (x, idx)))
                {
                    Stream? fileStream = null;
                    ReadOnlyCollection<DiscUtils.FileSystemInfo>? fsInfoList = null;

                    try
                    {
                        fileStream = Interop.Common.OpenPartitionByIdReadonly(volume.DeviceId, logger);
                        fsInfoList = FileSystemManager.DetectFileSystems(fileStream);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.Warning($"Failed to open FS on {volume.DeviceId} with error: {ex.Message}");
                        continue;
                    }

                    if (fsInfoList.Count > 1)
                    {
                        logger.Warning($"More than one FS is detected on volume {volume.DeviceId}, ignoring all but first");
                    }
                    else if (!fsInfoList.Any())
                    {
                        logger.Warning($"No filesystem found on {volume.DeviceId}, ignoring");
                        continue;
                    }

                    var fs = fsInfoList.First().Open(fileStream);
                    fsList.Add(fs);
                    disposableList.Add(fileStream);

                    var parentDeviceCaption = $"{volume.ParentDeviceId ?? "N/A"} <{volume.HardwareModel ?? "N/A"}>";

                    if (!tuiStructure.ContainsKey(parentDeviceCaption))
                    {
                        tuiStructure[parentDeviceCaption] = [];
                    }
                    
                    tuiStructure[parentDeviceCaption].Add([
                        volume.ParentDeviceId ?? "N/A",
                        volume.HardwareModel ?? "N/A",
                        volume.DeviceId,
                        fs.FriendlyName,
                        fs.VolumeLabel,
                        volume.MountPoints.Any() ? String.Join(", ", volume.MountPoints) : "N/A",
                        fs?.GetUuid(new NullLogger()) ?? "",
                        volume.Size.HasValue ? Utils.HumanizeFilesize(volume.Size.Value, true) : "N/A",
                        volume.BytesPerSector?.ToString() ?? "N/A"]);
                }

                if (Program.IsInteractive)
                {
                    if (!treeOutput)
                    {
                        var table = Utils.MakeConsoleTable([
                            "Parent Id", "HW Model", "Device Id", "FS Type", "Label", "Mount Points", "UUID", "Size", "Bytes per Sector"
                        ]);
                        
                        foreach (var driveDevice in tuiStructure.Keys)
                        {
                            foreach (var volumeInfo in tuiStructure[driveDevice])
                            {
                                table.AddRow(volumeInfo);
                            }
                        }

                        logger.Log(table);
                    }
                    else
                    {
                        var root = new Tree("Disk Drives");

                        foreach (var driveDevice in tuiStructure.Keys)
                        {
                            var driveNode = root.AddNode($"[yellow]{driveDevice}[/]");
                            var volumeTable = Utils.MakeConsoleTable([
                                "Device Id", "FS Type", "Label", "Mount Points", "UUID", "Size", "Bytes per Sector"
                            ]);

                            foreach (var volumeInfo in tuiStructure[driveDevice])
                            {
                                // Those are parent-related fields and would be the same for every row in a table when grouping
                                volumeTable.AddRow(volumeInfo.Skip(2).ToArray());
                            }

                            volumeTable.Collapse();
                            driveNode.AddNode(volumeTable);
                        }

                        logger.Log(root);
                    }
                }

                return new FsCollection(disposableList, GetFileSystems(fsList), "OS Volumes");
            });
        }

        public static FsCollection? MakeFsListFromVdisk(string path, ILog logger)
        {
            var probe = new DiskProbe(path, logger).Probe();

            if (probe.Disk is { ImageType: "qcow2" })
            {
                var stream = File.OpenRead(path);
                var qCowStream = new Bytes.Qcow2Stream(stream);
                var disk = new DiscUtils.Raw.Disk(qCowStream, Ownership.None);

                return new FsCollection(
                    new List<IDisposable> { stream, qCowStream, disk },
                    GetFileSystems(disk),
                    path);
            }
            else if (probe.Disk is { ImageType: "WIM" })
            {
                var stream = File.OpenRead(path);
                var wimFile = new WimFile(stream);
                var fsList = Enumerable.Range(0, wimFile.ImageCount).Select(x => wimFile.GetImage(x)).ToList();

                return new FsCollection(new List<IDisposable> { stream }, GetFileSystems(fsList), path);
            }
            else if (probe.Disk is { ImageType: "VHD" })
            {
                var disk = Vhd.Util.OpenDiskWithDu(path, logger).Unwrap();

                return new FsCollection(new List<IDisposable> { disk }, GetFileSystems(disk), path);
            }
            else if (probe.Disk is { ImageType: "RAW" })
            {
                var stream = File.OpenRead(path);
                var disk = new DiscUtils.Raw.Disk(stream, Ownership.None);

                return new FsCollection(
                    new List<IDisposable> { stream, disk },
                    GetFileSystems(disk),
                    path);
            }
            else if (probe.Disk != null)
            {
                var disk = DiscUtils.VirtualDisk.OpenDisk(path, FileAccess.Read);

                return new FsCollection(
                    new List<IDisposable> { disk }, GetFileSystems(disk), path);
            }
            else if (probe.Fs is not null)
            {
                var stream = File.OpenRead(path);
                var fileSystems = FileSystemManager.DetectFileSystems(stream).Select(x => x.Open(stream)).ToList();

                return new FsCollection(new List<IDisposable> { stream }, GetFileSystems(fileSystems), path);
            }
            else
            {
                return null;
            }
        }
    }
}