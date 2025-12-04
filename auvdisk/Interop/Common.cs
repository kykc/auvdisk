using auvdisk.Extensions;
using auvdisk.Log;
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

        internal static void RegisterPlatformSpecificVerbs(VerbHandlers handlers, ILog logger, IFlowContextHandler flowCtx)
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
                    .WithHandler(flowCtx)
                    .WithSuperUserRights()
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
                    var result = Flows.Val(rawOpts)
                        .WithHandler(flowCtx)
                        .WithSuperUserRights()
                        .Bind(opts => VhdMounter.Mount(opts.Target, logger));
                    
                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
            });

            // TODO: interactivity like in ChangePartitionType?
            handlers.Register((AssignVolumeLetter rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithSuperUserRights()
                    .Bind(opts => DriveLetterManager.AddDriveLetterToVolume(opts.Volume, opts.Letter.First(), logger));
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register((UnassignVolumeLetter rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithSuperUserRights()
                    .Bind(opts => DriveLetterManager.RemoveDriveLetterFromVolume(opts.Letter.First(), logger));
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register((CheckNtfsLastCluster rawOpts) =>
            {
                if (rawOpts.UseVss)
                {
                    using var result = Flows.Val(rawOpts)
                        .WithHandler(flowCtx)
                        .WithSuperUserRights()
                        .BindConcat(opts => Win32.Vss.Backup.Make(opts.Volume, logger), (opts, vss) => new { vss, opts })
                        .LogOk(logger, state => $"Created snapshot {state.vss.Root} for volume {state.opts.Volume}")
                        .MapConcat(
                            state => NtfsClone.TestLastNtfsCluster(new BlockDeviceUnbufferedStream(state.vss.Root, state.opts.GrantExtendedIoctl), logger),
                            (state, stream) => new { stream, state.vss, state.opts })
                        .MapDispose(state => new {state.vss, state.opts}, state => state.stream)
                        .LogOk(logger, state => $"Closing snapshot {state.vss.Root} for volume {state.opts.Volume}")
                        .MapDispose(state => state.opts, state => state.vss);
                    
                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
                else
                {
                    var result = Flows.Val(rawOpts)
                        .WithHandler(flowCtx)
                        .WithSuperUserRights()
                        .Map(opts => NtfsClone.TestLastNtfsCluster(new BlockDeviceUnbufferedStream(opts.Volume, opts.GrantExtendedIoctl), logger));
                    
                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
            });

            handlers.Register((InstallBootloader rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithSuperUserRights()
                    .Bind(opts => Win32.Util.InitializeEfiBootPartitionWithWinBcdBootloader(opts.Target, logger));
                
                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((ToggleEfi rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .BindConcat(
                        _ => GetVolumes(logger), 
                        (opts, volumes) => new { opts, volumes })
                    .BindConcat(
                        state => new BcdBootloaderInstaller(logger, state.volumes).FindBootableWindowsLayoutInMounted(state.opts.DriveLetter.First()),
                        (state, layout) => new { state.opts, state.volumes, layout })
                    .BindErr(state =>
                    {
                        var layout = state.layout;
                        
                        char? currentLetter = state.volumes
                            .Where(v => v.MountPoints.Contains(state.layout.EfiVolumePath))
                            .SelectMany(v => v.MountPoints)
                            .Where(m => m.Length <= 3)
                            .Select(m => new char?(m.ToUpper().First()))
                            .FirstOrDefault();
                        
                        if (currentLetter.IsSome())
                        {
                            logger.Log($"Dismounting <{currentLetter}>");
                            return DriveLetterManager.RemoveDriveLetterFromVolume(currentLetter!.Value, logger);
                        }
                        else
                        {
                            logger.Log($"Mounting {layout.EfiVolumePath} to <{layout.EfiTargetLetter}>");
                            return DriveLetterManager.AddDriveLetterToVolume(layout.EfiVolumePath, layout.EfiTargetLetter, logger);
                        }
                    });

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
#pragma warning restore CA1416
#endif
        }
    }
}