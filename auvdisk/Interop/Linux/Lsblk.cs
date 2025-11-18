using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using auvdisk.Cli;
using auvdisk.Extensions;
using auvdisk.Log;
using SimpleExec;

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
                x.BytesPerSectorLogical, 
                (x.Children ?? []).Select(c => c.Model).FirstOrDefault(),
                (x.Children ?? []).Select(c => $"/dev/{c.Name}").FirstOrDefault()
            )).ToList();
        }
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    public class BlockDevice
    {
        [JsonPropertyName("fstype")]
        public string? FsType { get; set; }
        
        [JsonPropertyName("model")]
        public string? Model { get; set; }

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
        string stdOut;

        try
        {
            (stdOut, _) = Command.ReadAsync("lsblk",
                    ["-sb", "--json", "--output", "fstype,name,mountpoints,uuid,label,size,phy-sec,log-sec,model"])
                .GetAwaiter()
                .GetResult();
        }
        catch (ExitCodeReadException ex)
        {
            return Flow<LsblkRoot>.Err($"Failed to fetch partition list: {ex.StandardError.Trim()}");
        }
        catch (Win32Exception ex)
        {
            return Flow<LsblkRoot>.Err($"Failed to launch lsblk: {ex.Message}");
        }

        var data = JsonSerializer.Deserialize<LsblkRoot>(stdOut);

        var allowedTypes = new[] { "vfat", "ntfs", "ext4", "squashfs" };

        if (data != null)
        {
            data.BlockDevices = data.BlockDevices
                .Where(x => x.FsType != null && allowedTypes.Contains(x.FsType))
                .ToList();

            return Flow<LsblkRoot>.Val(data);
        }
        else
        {
            return Flow<LsblkRoot>.Err("Failed to parse lsblk output");
        }
    }
}