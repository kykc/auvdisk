using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using auvdisk.Cli;
using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.Interop.Linux;

public static class Lsblk
{
    public class LsblkRoot
    {
        [JsonPropertyName("blockdevices")]
        public List<Lsblk.BlockDevice> BlockDevices { get; set; } = new();

        public IEnumerable<PhysicalVolumeInfo> ToVolumeInfos()
        {
            return BlockDevices.Select(x => new PhysicalVolumeInfo(
               $"/dev/{x.Name}",
                x.MountPoints.Where(m => m != null).Select(m => m!).ToList(),
                x.Size,
                x.BytesPerSectorLogical)).ToList();
        }
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    public class BlockDevice
    {
        [JsonPropertyName("fstype")]
        public string? FsType { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("mountpoints")]
        public List<string?> MountPoints { get; set; } = [];

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("children")]
        public List<BlockDevice>? Children { get; set; }

        [JsonPropertyName("size")]
        public UInt64? Size { get; set; }

        [JsonPropertyName("phy-sec")]
        public UInt32? BytesPerSectorPhysical { get; set; }

        [JsonPropertyName("log-sec")]
        public UInt32? BytesPerSectorLogical { get; set; }
    }

    public static Flow<LsblkRoot> GetPartitions(ILog logger)
    {
        var (stdOut, stdErr) = SimpleExec.Command.ReadAsync("lsblk",
            ["-sb", "--json", "--output", "fstype,name,mountpoints,uuid,label,size,phy-sec,log-sec"]).GetAwaiter().GetResult();

        var data = JsonSerializer.Deserialize<LsblkRoot>(stdOut);

        var allowedTypes = new[] { "vfat", "ntfs", "ext4", "squashfs" };

        if (data != null)
        {
            data.BlockDevices = data.BlockDevices
                .Where(x => x.FsType != null && allowedTypes.Contains(x.FsType))
                .ToList();

            return Flow<LsblkRoot>.Ok(data, logger);
        }
        else
        {
            return Flow<LsblkRoot>.Err("Failed to parse lsblk output", logger);
        }
    }
}