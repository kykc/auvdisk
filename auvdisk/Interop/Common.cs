using auvdisk.Extensions;
using auvdisk.Log;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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
        public static IComparer<string> GetDeviceIdComparer()
        {
            return Comparer<string>.Create((x, y) =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var regex = new Regex(@"Harddisk(?<diskIdx>\d+)Partition(?<partIdx>\d+)");
                    var matchX = regex.Match(x);
                    var matchY = regex.Match(y);

                    if (matchX.Success && matchY.Success)
                    {
                        x = 
                            Int32.Parse(matchX.Groups["diskIdx"].Value).ToString("D5") +
                            Int32.Parse(matchX.Groups["partIdx"].Value).ToString("D5");
                        
                        y =
                            Int32.Parse(matchY.Groups["diskIdx"].Value).ToString("D5") +
                            Int32.Parse(matchY.Groups["partIdx"].Value).ToString("D5");
                    }
                }
                
                return string.Compare(x, y, StringComparison.InvariantCultureIgnoreCase);
            });
        }
        
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