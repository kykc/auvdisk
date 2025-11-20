using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;
using auvdisk.Cli;
using auvdisk.DiskImage.Vhd;
using auvdisk.Interop;
using auvdisk.Log;
using Common.Logging;
using DiscUtils;
using DiscUtils.Streams;
using DiskAccessLibrary;
using Spectre.Console;
using Terminal.Gui;
using ILog = auvdisk.Log.ILog;
using Util = auvdisk.DiskImage.Vhd.Util;

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
#if DEBUG
        public const bool IsDebug = true;
#else
        public const bool IsDebug = false;
#endif
        public const bool UseCustomHelpRenderer = true;
        public static Action<string> DebugOutput { get; set; } = _ => { };
        
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

        private static Func<T, int> MakeCreateVdiskHandler<T>(ILog logger) where T: CreateVdisk
        {
            return (rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithCheckedSize(x => x.Size)
                    .WithCheckedTargetAvailable(x => x.Target, logger)
                    .WithCheckedTargetExtension(x => x.Target, x => x.IsVhdx() ? ".vhdx" : ".vhd")
                    .WithCheckedPartLayout(x => x.Partition, logger)
                    .Map(x => new { size = x.Size.ParseByteLength()!.Value, opts = x })
                    .BindConcat(
                        x => DiskImage.Util.CreateVdisk(x.opts.Target, x.size, logger, x.opts.IsDynamic(), x.opts.ZeroFillRequired(), x.opts.IsVhdx()),
                        (x, y) => new { x.size, vdisk = y, x.opts })
                    .MapDispose(x => new { x.size, x.opts }, x => x.vdisk)
                    .CheckDiscardIf(
                        x => x.opts.Partition != "",
                        x => PartitionTable.Util.InitializeDisk(x.opts.Target, x.size, x.opts.Partition, x.opts.Boot, logger))
                    .WithSideEffect(x => Utils.IfElse(
                        x.opts.IsVhdx, 
                        () => new DiskProbe(x.opts.Target, logger).Probe(), 
                        () => Util.OutputDiagnosticInfo(x.opts.Target, logger)));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            };
        }
        
        // Cannot make this work properly out of the box. This allows for tri-state logic in the following manner. Imagine bool? flag -v --verbose:
        // args: [] = Verbose = null
        // args: ["-v"] = Verbose => Verbose = true (TRes defaultValue, to be more specific) - this is exactly the case that is being fixed here
        // args: ["-v", "false"] => Verbose = false
        // args: ["-v", "true"] => Verbose = true
        private static TRes FixCommandLineParserNullable<T, TRes>(Func<T, TRes> mapper, string[] argOpts, T subject, string[] args, TRes defaultValue)
        {
            if (args.Intersect(argOpts).Any() && mapper(subject) == null)
            {
                return defaultValue;
            }
            else
            {
                return mapper(subject);
            }
        }
        
        static int Main(string[] args)
        {
            HandleEnvironment();
            
            DiscUtils.Complete.SetupHelper.SetupComplete();

            Patches.PatchManager.ApplyPatches();

            var logger = new Log.Logger();

            ParserResult<object>? cliResult = null;

#pragma warning disable CS0162 // Unreachable code detected
            if (UseCustomHelpRenderer)
            {
                var parser = new Parser(settings => settings.HelpWriter = null);
                cliResult = parser.ParseArguments(args, Cli.VerbHandlers.GetVerbTypes(true, false).ToArray());
                cliResult.WithNotParsed(errors => Cli.HelpRenderer.DisplayHelp(cliResult, errors, logger));
            }
            else
            {
                cliResult = Parser.Default.ParseArguments(args, Cli.VerbHandlers.GetVerbTypes(true, false).ToArray());
            }
#pragma warning restore CS0162 // Unreachable code detected

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

                return result.IsSuccess() ? 0 : 1;
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
                var probeResult = probe.Probe();

                return probeResult.IsSuccess() ? 0 : 1;
            });
            
            handlers.Register((Cli.VdiskCat opts) =>
            {
                ILog probeLogger = opts.Silent ? new NullLogger() : logger;
                var probe = new DiskProbe(opts.Source, probeLogger,
                    DiskProbe.GetCatArbitraryFile(opts.Target, logger, opts.Silent), opts.PartIdx);
                var probeResult = probe.Probe();

                return probeResult.IsSuccess() ? 0 : 1;
            });

            handlers.Register((Cli.LoopToVhd opts) =>
            {
                var result =
                    DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill, opts.NoBoot);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToLoop opts) =>
            {
                var result =
                    DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ImgToVhd opts) =>
            {
                var result = DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToImg opts) =>
            {
                var result = DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.CreateDiffVhd rawOpts) =>
            {
                void OutputDiskInfo(VhdFileInfo info)
                {
                    Util.OutputDiagnosticInfo(info.Path, logger);
                    new DiskProbe(info.Path, logger).Probe();
                }

                var result = Flow<CreateDiffVhd>.Val(rawOpts)
                    .WithCheckedTargetAvailable(opts => opts.Child, logger)
                    .WithCheckedSourceExists(opts => opts.Parent, logger)
                    .WithCheckedDiskType(_ => "VHD", opts => opts.Parent, opts => opts.Verbose, logger)
                    .Bind(x => Util.CreateDifferentialVhd(x.Parent, x.Child, logger))
                    .WithSideEffect(OutputDiskInfo)
                    .LogOk(logger, "Done.");

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register(MakeCreateVdiskHandler<CreateFixedVhd>(logger));
            handlers.Register(MakeCreateVdiskHandler<CreateDynamicVhd>(logger));
            
            handlers.Register((Cli.MergeVhd opts) =>
            {
                var result = DiskImage.Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger, opts.ZeroFill);

                return result.MapDispose(x => None.Value).LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ExtractFile rawOpts) =>
            {
                var result = Flow<ExtractFile>.Val(rawOpts)
                    .WithCheckedSourceExists(x => x.Source, logger)
                    .WithCheckedTargetAvailable(x => x.Target, logger)
                    .WithCheckedStreamBoundaries(x => x.Source, x => x.Offset, x => x.Length, logger)
                    .WithSideEffect(opts => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.DiagVhd rawOpts) =>
            {
                var result = Flow<DiagVhd>.Val(rawOpts)
                    .WithCheckedSourceExists(x => x.Source, logger)
                    .Map(opts => DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger).RefVal());

                return result.LogErrorIfAny(logger) || !result.UnwrapVal().Val ? 1 : 0;
            });
            
            handlers.Register((Cli.ResizeFixedVhd rawOpts) =>
            {
                var parseSize = (ResizeFixedVhd opts) =>
                {
                    var size = opts.Size.ParseByteLength()!.Value;
                    return new { size, opts };
                };

                var result = Flow<ResizeFixedVhd>.Val(rawOpts)
                    .WithCheckedSize(x => x.Size)
                    .WithCheckedSourceExists(x => x.Target, logger)
                    .Map(parseSize)
                    .Bind(x => Util.ResizeFixedVhd(x.opts.Target, x.size, logger, rawOpts.ZeroFill));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToVhdx opts) =>
            {
                var fixedValue = FixCommandLineParserNullable(x => x.Fixed, ["-f", "--fixed"], opts, args, true);
                
                var result =
                    DiskImageConverter.ConvertVhdToVhdx(opts.Source, opts.Target, logger, opts.Verbose, fixedValue, opts.ZeroFill);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdxToVhd opts) =>
            {
                var fixedValue = FixCommandLineParserNullable(x => x.Fixed, ["-f", "--fixed"], opts, args, true);
                
                var result = DiskImageConverter.ConvertVhdxToVhd(opts.Source, opts.Target, logger, opts.Verbose, fixedValue, opts.ZeroFill);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.GenVmdkWrapper rawOpts) =>
            {
                var result = Flow<GenVmdkWrapper>.Val(rawOpts)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .WithCheckedDiskType(_ => "RAW", opts => opts.Source, _ => false, logger)
                    .MapOr(opts => new { opts, vmdk = DiskImage.Vmdk.VmdkFlatWrapper.Create(opts.Source, logger) }, "Failed to create VMDK wrapper")
                    .WithSideEffect(x =>
                    {
                        Utils.If(() => logger.Log(new Rule("[green]Resulting VMDK[/]").LeftJustified()),
                            () => !x.opts.Silent);
                        logger.Log(x.vmdk?.ToString().EscapeMarkup() ?? ""); // On error logger will contain the reason already
                        Utils.If(() => logger.Log(new Rule("[green]End of VMDK[/]").LeftJustified()),
                            () => !x.opts.Silent);
                        Utils.If(
                            () => logger.Log(
                                "Put that into a file, place it into the same folder as the source image and you're good to go"),
                            () => !x.opts.Silent);
                    });

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.Qcow2ToRaw opts) =>
            {
                var result = DiskImageConverter.ConvertQcow2ToRaw(opts.Source, opts.Target, logger, opts.Verbose);

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ProbeBcd rawOpts) =>
            {
                var result = Flow<ProbeBcd>.Val(rawOpts)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .WithSideEffect(opts => BCD.Util.ProbeBcd(opts.Source, opts.Verbose, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVdisk rawOpts) =>
            {
                var result = Flow<BrowseVdisk>.Val(rawOpts)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .WithSideEffect(opts => Commander.FsCommander.OpenDiskImage(opts.Source, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVolumes opts) =>
            {
                if (opts.List)
                {
                    var result = DiskImage.Factory.MakeFsListFromAvailableVolumes(logger, opts.TreeOutput);

                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
                else
                {
                    var result = Commander.FsCommander.OpenLocalFs(logger);

                    return result.LogErrorIfAny(logger) ? 1 : 0;
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
            
#if DEBUG
            handlers.Register((DebugRepl opts) =>
            {
                Dbg.CSharpRepl.EntryPoint();

                return 0;
            });
#endif

            Interop.Common.RegisterPlatformSpecificVerbs(handlers, logger);
            
            int exitCode = handlers.HandleParserResult(cliResult);
            
            Console.Out.Flush();
            Console.Error.Flush();

            return exitCode;
        }
        
        
    }
}
