using auvdisk.Extensions;
using auvdisk.Log;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace auvdisk.Interop
{
    public record PhysicalVolumeInfo(
        [Display(Name = "Device Id")]
        string DeviceId,
        [Display(Name = "Mount Points")]
        List<string> MountPoints,
        [Display(Name = "Size")]
        ulong? Size,
        [Display(Name = "Bytes per Sector")]
        uint? BytesPerSector);

    public static class Common
    {
        public static Flow<IEnumerable<PhysicalVolumeInfo>> GetVolumes(ILog logger)
        {
#if WINDOWS
#pragma warning disable CA1416
            return Win32.Util.GetVolumeList(logger);
#pragma warning restore CA1416
#else
            return Linux.Lsblk.GetPartitions(logger).Map(x => x.ToVolumeInfos());
#endif
        }

        public static Stream OpenPartitionByIdReadonly(string name, ILog logger)
        {
#if WINDOWS
#pragma warning disable CA1416
            return Win32.Util.OpenVolumeByDeviceIdReadOnly(name, logger);

#pragma warning restore CA1416
#else
            return Linux.Util.OpenPartitionByName(name, logger);
#endif
        }
    }
}