using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;
using auvdisk.Cli;
using auvdisk.Log;
using Common.Logging;
using DiscUtils;
using DiscUtils.Streams;
using Spectre.Console;
using Terminal.Gui;
using ILog = auvdisk.Log.ILog;

namespace auvdisk
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Program
    {
        public static readonly TimeSpan ProgressReportRate = TimeSpan.FromMilliseconds(200);
        public static bool IsInteractive = true;
        public static LogLevel LogLevel { get; private set; } = LogLevel.Info;
#if WINDOWS
        public const bool IsWindows = true;
#else
        public const bool IsWindows = false;
#endif
        private static void HandleEnvironment()
        {
            if ((Environment.GetEnvironmentVariable("AUVDISK_LOG_LEVEL") ?? "").ToLowerInvariant() == "debug")
            {
                LogLevel = LogLevel.Debug;
            }

            if ((Environment.GetEnvironmentVariable("AUVDISK_FRONTEND") ?? "").ToLowerInvariant() == "noninteractive")
            {
                IsInteractive = false;
            }
        }
        
        static int Main(string[] args)
        {
            HandleEnvironment();
            
            DiscUtils.Complete.SetupHelper.SetupComplete();

            Patches.PatchManager.ApplyPatches();

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

            var cliResult = Parser.Default.ParseArguments(args, Cli.VerbHandlers.GetVerbs().ToArray());

            var handlers = new Cli.VerbHandlers();
            
            handlers.Register((Cli.VdiskProbe opts) =>
            {
                var recursiveHandler =
                    DiskProbe.GetWalkFsRecursive((fs, f) => logger.Log("/" + f.FormatDuPath()));

                var probe = new DiskProbe(opts.Source, logger, opts.Recursive ? recursiveHandler : null, opts.PartIdx);
                var result = probe.Probe();

                if (result.Disk?.ImageType == "VHD" && opts.Verbose)
                {
                    DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);
                }

                return 0;
            });

            handlers.Register((Cli.VdiskList opts) =>
            {
                Regex? filterRegex = null;
                var filterEnabled = args.Contains("--filter");

                if (opts.Recursive && filterEnabled)
                {
                    filterRegex = new Regex(opts.Filter != String.Empty
                        ? opts.Filter
                        : AnsiConsole.Prompt(new TextPrompt<string>("regex [yellow]?>[/] ")));
                }
                else if (filterEnabled)
                {
                    logger.Warning("Filter option is ignored in non-recursive mode");
                }

                ILog probeLogger = opts.Silent ? new NullLogger() : logger;
                var fsHandler =
                    DiskProbe.GetListArbitraryDir(opts.Target, logger, opts.Silent, opts.Recursive, filterRegex);
                var probe = new DiskProbe(opts.Source, probeLogger, fsHandler, opts.PartIdx);
                probe.Probe();

                return 0;
            });
            
            handlers.Register((Cli.VdiskCat opts) =>
            {
                ILog probeLogger = opts.Silent ? new NullLogger() : logger;
                var probe = new DiskProbe(opts.Source, probeLogger,
                    DiskProbe.GetCatArbitraryFile(opts.Target, logger, opts.Silent), opts.PartIdx);
                probe.Probe();

                return 0;
            });

            handlers.Register((Cli.LoopToVhd opts) =>
            {
                var result =
                    DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToLoop opts) =>
            {
                var result =
                    DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.ImgToVhd opts) =>
            {
                var result = DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToImg opts) =>
            {
                var result = DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.CreateDiffVhd opts) =>
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
            });
            
            handlers.Register((Cli.CreateFixedVhd opts) =>
            {
                var action = () =>
                {
                    var size = opts.Size.ParseByteLength()!.Value;
                    DiskImage.Vhd.Util.CreateFixedVhd(opts.Target, size, logger, opts.ZeroFill);
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSize(opts.Size)
                    .WithCheckedTargetAvailable(opts.Target)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.MergeVhd opts) =>
            {
                using var result = DiskImage.Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.CreateDynamicVhd opts) =>
            {
                var action = () =>
                {
                    var size = opts.Size.ParseByteLength()!.Value;
                    DiskImage.Vhd.Util.CreateDynamicVhd(opts.Target, size, logger);
                    DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Target, logger);
                    new DiskProbe(opts.Target, logger).Probe();
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSize(opts.Size)
                    .WithCheckedTargetAvailable(opts.Target)
                    .WithSideEffect(action)
                    .Log("Done.");

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.ExtractFile opts) =>
            {
                var action = () => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length);

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSourceExists(opts.Source)
                    .WithCheckedTargetAvailable(opts.Target)
                    .WithCheckedStreamBoundaries(opts.Source, opts.Offset, opts.Length)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.DiagVhd opts) =>
            {
                bool validVhd = true;
                var action = () =>
                {
                    validVhd = DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSourceExists(opts.Source)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() || !validVhd ? 1 : 0;
            });
            
            handlers.Register((Cli.ResizeFixedVhd opts) =>
            {
                Action action = () =>
                {
                    var size = opts.Size.ParseByteLength()!.Value;
                    DiskImage.Vhd.Util.ResizeFixedVhd(opts.Target, size, logger);
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSize(opts.Size)
                    .WithCheckedSourceExists(opts.Target)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.CheckIsSparse opts) =>
            {
                var result = Fs.Util.IsSparseFile(opts.Target, logger);

                if (result != null)
                {
                    logger.Log($"Is sparse file: {result}");
                }

                return result == null ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToVhdx opts) =>
            {
                var result =
                    DiskImageConverter.ConvertVhdToFixedVhdx(opts.Source, opts.Target, logger, opts.Verbose);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdxToVhd opts) =>
            {
                var result = DiskImageConverter.ConvertVhdxToFixedVhd(opts.Source, opts.Target, logger, opts.Verbose);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.GenVmdkWrapper opts) =>
            {
                bool success = false;

                var action = () =>
                {
                    var vmdk = DiskImage.Vmdk.VmdkFlatWrapper.Create(opts.Source, logger);

                    if (vmdk != null)
                    {
                        // Using Console (not logger) here on purpose. I'm afraid something might break Spectre.Console markup handling at some moment
                        Utils.If(() => logger.Log(new Rule("[green]Resulting VMDK[/]").LeftJustified()),
                            () => !opts.Silent);
                        Console.WriteLine(vmdk?.ToString() ?? ""); // On error logger will contain the reason already
                        Utils.If(() => logger.Log(new Rule("[green]End of VMDK[/]").LeftJustified()),
                            () => !opts.Silent);
                        Utils.If(
                            () => logger.Log(
                                "Put that into a file, place it into the same folder as the source image and you're good to go"),
                            () => !opts.Silent);
                    }

                    success = vmdk != null;
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSourceExists(opts.Source)
                    .WithCheckedDiskType("RAW", opts.Source, false)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() && success ? 1 : 0;
            });
            
            handlers.Register((Cli.Qcow2ToRaw opts) =>
            {
                var result = DiskImageConverter.ConvertQcow2ToRaw(opts.Source, opts.Target, logger, opts.Verbose);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.ProbeBcd opts) =>
            {
                var action = () =>
                {
                    auvdisk.BCD.Util.ProbeBcd(opts.Source, opts.Verbose, logger);
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSourceExists(opts.Source)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVdisk opts) =>
            {
                var action = () =>
                {
                    Commander.FsCommander.OpenDiskImage(opts.Source, logger);
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedSourceExists(opts.Source)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVolumes opts) =>
            {
                if (opts.List)
                {
                    var result = DiskImage.Factory.MakeFsListFromAvailableVolumes(logger);

                    return result.LogErrorIfAny() ? 1 : 0;
                }
                else
                {
                    var result = Commander.FsCommander.OpenLocalFs(logger);

                    return result.LogErrorIfAny() ? 1 : 0;
                }
            });

            handlers.Register((Cli.ChangePartitionType opts) =>
            {
                if (opts.DiskNumber == 0 || opts.PartitionNumber == 0 || opts.PartitionType == "")
                {
                    var fsListResult = Factory.MakeFsListFromAvailableVolumes(logger);

                    if (fsListResult.IsError())
                    {
                        fsListResult.LogErrorIfAny();
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
                
                string partTypeGuid = "";
                
                if (opts.PartitionType.ToLowerInvariant() == "data")
                {
                    partTypeGuid = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
                }
                else if (opts.PartitionType.ToLowerInvariant() == "efi")
                {
                    partTypeGuid = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
                }
                else
                {
                    logger.Error("Unknown partition type: " + opts.PartitionType);
                    return 1;
                }
                
                string result =
                    $"\"select disk {opts.DiskNumber}\", \"select partition {opts.PartitionNumber}\", \"set id={partTypeGuid} override\" | diskpart";
                
                logger.Log($"[green]{result}[/]");

                return 0;
            });

            int exitCode = handlers.HandleParserResult(cliResult);
            
            Console.Out.Flush();
            Console.Error.Flush();

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
