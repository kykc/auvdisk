using auvdisk.Extensions;
using auvdisk.Log;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using auvdisk.Cli;
using auvdisk.DiskImage;
#if WINDOWS
using auvdisk.Fs.Ntfs;
using auvdisk.Interop.Win32;
#endif
using Spectre.Console;

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
        uint? BytesPerSector,
        [Display(Name = "HW Model")]
        string? HardwareModel,
        [Display(Name = "Parent")]
        string? ParentDeviceId);

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

        internal static void RegisterPlatformSpecificVerbs(VerbHandlers handlers, ILog logger)
        {
#if WINDOWS
#pragma warning disable CA1416
            handlers.Register((Cli.CheckIsSparse opts) =>
            {
                var result = Fs.Util.IsSparseFile(opts.Target, logger);

                if (result != null)
                {
                    logger.Log($"Is sparse file: {result}");
                }

                return result == null ? 1 : 0;
            });

            handlers.Register((Cli.ChangePartitionType opts) =>
            {
                if (opts.DiskNumber == 0 || opts.PartitionNumber == 0 || opts.PartitionType == "")
                {
                    var fsListResult = Factory.MakeFsListFromAvailableVolumes(logger, true);

                    if (fsListResult.IsErr)
                    {
                        fsListResult.LogErrorIfAny(logger);
                        return 1;
                    }
                }

                if (opts.DiskNumber == 0)
                {
                    opts.DiskNumber = AnsiConsole.Prompt(new TextPrompt<int>("disk number [yellow]?>[/] "));
                }

                if (opts.PartitionNumber == 0)
                {
                    opts.PartitionNumber = AnsiConsole.Prompt(new TextPrompt<int>("partition number [yellow]?>[/] "));
                }

                if (opts.PartitionType == "")
                {
                    opts.PartitionType = AnsiConsole.Prompt(new TextPrompt<string>("partition type [yellow]?>[/] "));
                }
                
                var result = DiskpartScriptMananger.GenerateSetidScript(opts.PartitionType, opts.DiskNumber, opts.PartitionNumber, logger);

                if (result.IsErr)
                {
                    result.LogErrorIfAny(logger);
                    return 1;
                }
                else
                {
                    logger.Log($"[green]{result.UnwrapVal()}[/]");
                    
                    if (opts.Yes || AnsiConsole.Confirm("Execute generated script?"))
                    {
                        var executeResult = DiskpartScriptMananger.Execute(result.UnwrapVal(), logger);

                        if (executeResult.IsErr)
                        {
                            executeResult.LogErrorIfAny(logger);

                            return 1;
                        }
                        else
                        {
                            logger.Log(executeResult.UnwrapVal().StandardOutput);
                        }
                    }
                    
                    return 0;
                }
            });
            
            handlers.Register((CloneLiveVolumeToVhd rawOpts) =>
            {
                CloneLiveVolumeToVhd? NormalizeDriveLetter(CloneLiveVolumeToVhd subject)
                {
                    var regex = new Regex(@"^[a-zA-Z]{1}[\:]?[\\]?$");

                    if (regex.IsMatch(subject.Source))
                    {
                        subject.Source = subject.Source[0].ToString().ToUpperInvariant() + ":\\";
                        return subject;
                    }
                    
                    return null;
                };
                
                var result = Flows.Val(rawOpts)
                    .WithCheckedTargetAvailable((opts) => opts.Target, logger)
                    .WithCheckedTargetExtension(opts => opts.Target, opts => opts.Vhdx ? ".vhdx" : ".vhd")
                    .MapOr(NormalizeDriveLetter, "Invalid source volume path")
                    .Bind(opts => Interop.Win32.Util.CloneVolumeToVirtualDiskWithVss(opts.Source, opts.Target, logger, opts.Fixed, opts.ZeroFill, opts.Bootable, opts.Vhdx));
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register((MountVhdX rawOpts) =>
            {
                if (rawOpts.Dismount)
                {
                    return Interop.Win32.VhdMounter.Dismount(rawOpts.Target, logger) ? 0 : 1;
                }
                else
                {
                    var result = Interop.Win32.VhdMounter.Mount(rawOpts.Target, logger);
                    
                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
            });

            // TODO: interactivity like in ChangePartitionType?
            handlers.Register((AssignVolumeLetter rawOpts) =>
            {
                var result = Interop.Win32.DriveLetterManager.AddDriveLetterToVolume(rawOpts.Volume, rawOpts.Letter.First(), logger);
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register((UnassignVolumeLetter rawOpts) =>
            {
                var result = Interop.Win32.DriveLetterManager.RemoveDriveLetterFromVolume(rawOpts.Letter.First(), logger);
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register((CheckNtfsLastCluster opts) =>
            {
                NtfsClone.TestLastNtfsCluster(new BlockDeviceUnbufferedStream(opts.Volume, opts.GrantExtendedIoctl), logger);

                return 0;
            });
#pragma warning restore CA1416
#endif
        }
    }
}