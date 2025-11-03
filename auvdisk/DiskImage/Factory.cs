using auvdisk.Log;
using DiscUtils;
using DiscUtils.Streams;
using DiscUtils.Wim;

namespace auvdisk.DiskImage
{
    public class FsList : IDisposable
    {
        public IEnumerable<IDisposable> Entities { get; }
        public IEnumerable<DiscFileSystem> FileSystems { get; }

        public FsList(IEnumerable<IDisposable> entities, IEnumerable<DiscFileSystem> fileSystems)
        {
            Entities = entities;
            FileSystems = fileSystems;
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
        private static IEnumerable<DiscFileSystem> GetFileSystems(DiscUtils.VirtualDisk disk)
        {
            var volumeManager = new VolumeManager(disk);
            var fsList = volumeManager.GetPhysicalVolumes()
                .Select(volume => (volume, fsInfoList: FileSystemManager.DetectFileSystems(volume.Open())))
                .Select(x => x.fsInfoList.Select(fsInfo => fsInfo.Open(x.volume)))
                .SelectMany(x => x).ToList();

            return fsList;
        }

        public static FsList? MakeFsList(string path, ILog logger)
        {
            var probe = new DiskProbe(path, logger).Probe();

            if (probe.Disk is { ImageType: "qcow2" })
            {
                var stream = File.OpenRead(path);
                var qCowStream = new Bytes.Qcow2Stream(stream);
                var disk = new DiscUtils.Raw.Disk(qCowStream, Ownership.None);

                return new FsList(
                    new List<IDisposable> { stream, qCowStream, disk },
                    GetFileSystems(disk));
            }
            else if (probe.Disk is { ImageType: "WIM" })
            {
                var stream = File.OpenRead(path);
                var wimFile = new WimFile(stream);
                var fsList = Enumerable.Range(0, wimFile.ImageCount).Select(x => wimFile.GetImage(x)).ToList();

                return new FsList(new List<IDisposable> { stream }, fsList);
            }
            else if (probe.Disk is { ImageType: "VHD" })
            {
                var disk = Vhd.Util.OpenDiskWithDu(path, logger)!;

                return new FsList(new List<IDisposable> { disk }, GetFileSystems(disk));
            }
            else if (probe.Disk is { ImageType: "RAW" })
            {
                var stream = File.OpenRead(path);
                var disk = new DiscUtils.Raw.Disk(stream, Ownership.None);

                return new FsList(
                    new List<IDisposable> { stream, disk },
                    GetFileSystems(disk));
            }
            else if (probe.Disk != null)
            {
                var disk = DiscUtils.VirtualDisk.OpenDisk(path, FileAccess.Read);

                return new FsList(
                    new List<IDisposable> { disk }, GetFileSystems(disk));
            }
            else if (probe.Fs is not null)
            {
                var stream = File.OpenRead(path);
                var fileSystems = FileSystemManager.DetectFileSystems(stream).Select(x => x.Open(stream)).ToList();

                return new FsList(new List<IDisposable> { stream }, fileSystems);
            }
            else
            {
                return null;
            }
        }
    }
}