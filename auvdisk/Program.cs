using System.Runtime.InteropServices;
using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;
using auvdisk.Cli;
using auvdisk.Log;
using DiscUtils;
using DiscUtils.BootConfig;
using DiscUtils.Registry;
using DiscUtils.Streams;
using Spectre.Console;

namespace auvdisk
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Program
    {
        static int Main(string[] args)
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();

            var logger = new Log.Logger();

            // Special case for launching self with Admin privileges to create large file fast.
            // Somewhat hacky, we don't populate those CLI arguments in help as they're
            // for internal use only
            if (HandleResizeFileUnsafe(args, logger))
            {
                return 0;
            }
            else if (HandleMarkdownHelp(args, logger))
            {
                return 0;
            }

            var cliResult = Parser.Default.ParseArguments<
                Cli.VdiskProbe, Cli.VdiskList, Cli.VdiskCat, Cli.LoopToVhd, 
                Cli.VhdToLoop, Cli.ImgToVhd, Cli.VhdToImg, Cli.CreateDiffVhd, 
                Cli.CreateFixedVhd, Cli.MergeVhd, Cli.CreateDynamicVhd, Cli.ExtractFile,
                Cli.DiagVhd, Cli.ResizeFixedVhd, Cli.CheckIsSparse, Cli.VhdToVhdx,
                Cli.VhdxToVhd, Cli.GenVmdkWrapper>(args);

            var exitCode = cliResult.MapResult(
                (Cli.VdiskProbe opts) =>
                {
                    var probe = new DiskProbe(opts.Source, logger);
                    var result = probe.Probe();

                    if (result.Disk?.ImageType == "VHD" && opts.Verbose)
                    {
                        DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);
                    }

                    return 0;
                },
                (Cli.VdiskList opts) =>
                {
                    var probe = new DiskProbe(opts.Source, logger, DiskProbe.GetListArbitraryDir(opts.Target, logger));
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskCat opts) =>
                {
                    var probe = new DiskProbe(opts.Source, logger, DiskProbe.GetCatArbitraryFile(opts.Target, logger));
                    probe.Probe();

                    return 0;
                },
                (Cli.LoopToVhd opts) =>
                {
                    var result = DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.VhdToLoop opts) =>
                {
                    var result = DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.ImgToVhd opts) =>
                {
                    var result = DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.VhdToImg opts) =>
                {
                    var result = DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose);

                    return result.LogErrorIfAny() ?  1 : 0;
                },
                (Cli.CreateDiffVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateDifferentialVhd(opts.Parent, opts.Child, logger);
                        DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Child, logger);
                        new DiskProbe(opts.Child, logger).Probe();
                    };

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedTargetAvailable(opts.Child)
                        .WithCheckedSourceExists(opts.Parent)
                        .WithCheckedDiskType("VHD", opts.Parent, opts.Verbose)
                        .WithSideEffect(action)
                        .Log("Done.");

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.CreateFixedVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateFixedVhd(opts.Target, opts.Size, logger, opts.ZeroFill);
                    };

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedTargetAvailable(opts.Target)
                        .WithSideEffect(action);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.MergeVhd opts) =>
                {
                    using var result = DiskImage.Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.CreateDynamicVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateDynamicVhd(opts.Target, opts.Size, logger);
                        DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Target, logger);
                        new DiskProbe(opts.Target, logger).Probe();
                    };

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedTargetAvailable(opts.Target)
                        .WithSideEffect(action)
                        .Log("Done.");

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.ExtractFile opts) =>
                {
                    var action = () => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length);

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedSourceExists(opts.Source)
                        .WithCheckedTargetAvailable(opts.Target)
                        .WithCheckedStreamBoundaries(opts.Source, opts.Offset, opts.Length)
                        .WithSideEffect(action);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.DiagVhd opts) =>
                {
                    var action = () => DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedSourceExists(opts.Source)
                        .WithSideEffect(action);
                    
                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.ResizeFixedVhd opts) =>
                {
                    Action action = () => DiskImage.Vhd.Util.ResizeFixedVhd(opts.Target, opts.Size, logger);

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedSourceExists(opts.Target)
                        .WithSideEffect(action);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.CheckIsSparse opts) =>
                {
                    var result = Fs.Util.IsSparseFile(opts.Target, logger);

                    if (result != null)
                    {
                        logger.Log($"Is sparse file: {result}");
                    }

                    return result == null ? 1 : 0;
                },
                (Cli.VhdToVhdx opts) =>
                {
                    var result =
                        DiskImageConverter.ConvertVhdToFixedVhdx(opts.Source, opts.Target, logger, opts.Verbose);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.VhdxToVhd opts) =>
                {
                    var result = DiskImageConverter.ConvertVhdxToFixedVhd(opts.Source, opts.Target, logger, opts.Verbose);

                    return result.LogErrorIfAny() ? 1 : 0;
                },
                (Cli.GenVmdkWrapper opts) =>
                {
                    bool success = false;

                    var action = () =>
                    {
                        var vmdk = DiskImage.Vmdk.VmdkFlatWrapper.Create(opts.Source, logger);

                        if (vmdk != null)
                        {
                            // Using Console (not logger) here on purpose. I'm afraid something might break Spectre.Console markup handling at some moment
                            logger.Log(new Rule("[green]Resulting VMDK[/]").LeftJustified());
                            Console.WriteLine(vmdk.ToString()); // On error logger will contain the reason already
                            logger.Log(new Rule("[green]End of VMDK[/]").LeftJustified());
                            logger.Log("Put that into a file, place it into the same folder as the source image and you're good to go");
                        }

                        success = vmdk != null;
                    };

                    var result = Flow<None>.Ok(None.Value, logger)
                        .WithCheckedSourceExists(opts.Source)
                        .WithCheckedDiskType("RAW", opts.Source, false)
                        .WithSideEffect(action);

                    return result.LogErrorIfAny() && success ? 1 : 0;
                },
                _ => 2
            );

            return exitCode;
        }

        public static bool HandleMarkdownHelp(string[] args, Log.ILog logger)
        {
            if (args is ["markdown-help"])
            {
                var types = Cli.Util.GetTypesWithAttribute<VerbAttribute>()
                    .Select(t => t.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(VerbAttribute)))
                    .Select(v => new
                    {
                        VerbName = v!.ConstructorArguments.First().Value,
                        HelpText = v.NamedArguments.Where(x => x.MemberName == "HelpText")!.First().TypedValue.Value
                    });

                Console.WriteLine(auvdisk.Text.MarkdownGenerator.ToMarkdownTable(types, logger));

                return true;
            }

            return false;
        }

        public static bool HandleResizeFileUnsafe(string[] args, Log.ILog logger)
        {
            if (args is ["resize-file-unsafe", _, _])
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    bool success = false;
#if WINDOWS
                    try
                    {
                        logger.Log($"Administrator privileges: {Environment.IsPrivilegedProcess}");
                        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                        using var privilege =
                            new Win32.TokenPrivileges.AdjustPrivilege(Win32.TokenPrivileges.PrivilegeName.SeManageVolumePrivilege);

                        bool canManagerVolume = Win32.TokenPrivileges.PrivilegeProvider.HasPrivilege(null,
                            currentProcess, Win32.TokenPrivileges.PrivilegeName.SeManageVolumePrivilege);

                        logger.Log($"SeManageVolumePrivilege: {canManagerVolume}");

                        success = Fs.Util.ResizeFileFastUnsafe(args[1], ulong.Parse(args[2]), logger);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(Spectre.Console.Markup.Escape(ex.Message));
                    }
#endif
                    if (!success)
                    {
                        logger.Log("Falling back to slow mode");
                        Fs.Util.ResizeFile(args[1], ulong.Parse(args[2]), logger);
                    }
                }
                else
                {
                    Fs.Util.ResizeFile(args[1], ulong.Parse(args[2]), logger);
                }

                return true;
            }

            return false;
        }
    }
}
