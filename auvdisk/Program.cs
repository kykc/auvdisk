using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;
using auvdisk.Cli;
using auvdisk.Interop;
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

            var cliResult = Parser.Default.ParseArguments(args, Cli.VerbHandlers.GetVerbTypes(true, false).ToArray());

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
                var outputDiskInfo = () =>
                {
                    DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Child, logger);
                    new DiskProbe(opts.Child, logger).Probe();
                };

                var result = Flow<None>.Ok(None.Value, logger)
                    .WithCheckedTargetAvailable(opts.Child)
                    .WithCheckedSourceExists(opts.Parent)
                    .WithCheckedDiskType("VHD", opts.Parent, opts.Verbose)
                    .Bind(_ => DiskImage.Vhd.Util.CreateDifferentialVhd(opts.Parent, opts.Child, logger))
                    .WithSideEffect(outputDiskInfo)
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
                var action = () => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length, logger);

                var result = Flow<ExtractFile>.Ok(opts, logger)
                    .WithCheckedSourceExists(x => x.Source)
                    .WithCheckedTargetAvailable(x => x.Target)
                    .WithCheckedStreamBoundaries(x => x.Source, x => x.Offset, x => x.Length)
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

                var result = Flow<DiagVhd>.Ok(opts, logger)
                    .WithCheckedSourceExists(x => x.Source)
                    .WithSideEffect(action);

                return result.LogErrorIfAny() || !validVhd ? 1 : 0;
            });
            
            handlers.Register((Cli.ResizeFixedVhd rawOpts) =>
            {
                var parseSize = (ResizeFixedVhd opts) =>
                {
                    var size = opts.Size.ParseByteLength()!.Value;
                    return new { size, opts };
                };

                var result = Flow<ResizeFixedVhd>.Ok(rawOpts, logger)
                    .WithCheckedSize(x => x.Size)
                    .WithCheckedSourceExists(x => x.Target)
                    .Map(parseSize)
                    .Bind(x => DiskImage.Vhd.Util.ResizeFixedVhd(x.opts.Target, x.size, logger, rawOpts.ZeroFill));

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToVhdx opts) =>
            {
                var result =
                    DiskImageConverter.ConvertVhdToVhdx(opts.Source, opts.Target, logger, opts.Verbose, opts.Fixed, opts.ZeroFill);

                return result.LogErrorIfAny() ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdxToVhd opts) =>
            {
                var result = DiskImageConverter.ConvertVhdxToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.Fixed, opts.ZeroFill);

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
                        Utils.If(() => logger.Log(new Rule("[green]Resulting VMDK[/]").LeftJustified()),
                            () => !opts.Silent);
                        logger.Log(vmdk?.ToString().EscapeMarkup() ?? ""); // On error logger will contain the reason already
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
                    var result = DiskImage.Factory.MakeFsListFromAvailableVolumes(logger, opts.TreeOutput);

                    return result.LogErrorIfAny() ? 1 : 0;
                }
                else
                {
                    var result = Commander.FsCommander.OpenLocalFs(logger);

                    return result.LogErrorIfAny() ? 1 : 0;
                }
            });

            handlers.Register((OutMarkdownHelp opts) =>
            {
                var types = VerbHandlers.GetVerbTypes(false, true)
                    .Select(t => t.GetCustomAttribute<VerbAttribute>())
                    .Where(t => t is { Hidden: false })
                    .Select(v => new
                    {
                        VerbName = v!.Name,
                        HelpText = v!.HelpText,
                    });

                Console.WriteLine(Text.MarkdownGenerator.ToMarkdownTable(types, logger));

                return 0;
            });

            handlers.Register((ResizeFileUnsafe opts) =>
            {
                var success = Fs.Util.HandleResizeFile(opts.Target, opts.Size, opts.ZeroFill, logger);

                return success ? 0 : 1;
            });

            Interop.Common.RegisterPlatformSpecificVerbs(handlers, logger);

            int exitCode = handlers.HandleParserResult(cliResult);
            
            Console.Out.Flush();
            Console.Error.Flush();

            return exitCode;
        }
        
        
    }
}
