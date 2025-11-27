using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;
using auvdisk.Cli;
using auvdisk.Log;
using Common.Logging;
using Spectre.Console;
using ILog = auvdisk.Log.ILog;
using Util = auvdisk.DiskImage.Vhd.Util;

namespace auvdisk
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Program
    {
        public static readonly TimeSpan ProgressReportRate = TimeSpan.FromMilliseconds(200);
        public static bool IsInteractive = true;
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
        [SuppressMessage("ReSharper", "HeuristicUnreachableCode")] 
        public static LogLevel LogLevel { get; private set; } = IsDebug ? LogLevel.Debug : LogLevel.Info;
        
        public const bool UseCustomHelpRenderer = true;
        public static Action<string> DebugOutput { get; set; } = _ => { };
        
        public static Func<Exception, bool> ExceptionFilter { get; set; } = TestExceptionFilter.ShouldCatch;
        
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

        private static Func<T, int> MakeCreateVdiskHandler<T>(ILog logger, IFlowContextHandler ctx) where T: CreateVdisk
        {
            return (rawOpts) =>
            {
                using var result = Flows.Val(rawOpts)
                    .WithHandler(ctx)
                    .WithCheckedSize(x => x.Size)
                    .WithCheckedTargetAvailable(x => x.Target, logger)
                    .WithCheckedTargetExtension(x => x.Target, x => x.IsVhdx() ? ".vhdx" : ".vhd")
                    .WithCheckedPartLayout(x => x.Partition, logger)
                    .Map(x => new { size = x.Size.ParseByteLength()!.Value, opts = x })
                    .BindConcat(
                        x => DiskImage.Util.CreateVdisk(x.opts.Target, x.size, logger, x.opts.IsDynamic(), x.opts.ZeroFillRequired(), x.opts.IsVhdx()),
                        (x, y) => new { x.size, vdisk = y, x.opts })
                    .MapDispose(x => new { x.size, x.opts }, x => x.vdisk)
                    .BindErrIf(
                        x => x.opts.Partition != "",
                        x => PartitionTable.Util.InitializeDisk(x.opts.Target, x.size, x.opts.Partition, x.opts.Boot, logger))
                    .SideEffect(x => Utils.IfElse(
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
            
            var unexpectedExceptionHandler = (Exception ex) =>
            {
                logger.Debug($"[yellow][[DEBUG]][/] [red]Unhandled exception [underline]<{ex.GetType().ToString().EscapeMarkup()}>[/] with message:[/] [yellow]{ex.Message.EscapeMarkup()}[/]");
                logger.Debug(ex.StackTrace?.EscapeMarkup() ?? "Stack trace missing");

                return ex.Message;
            };
            var flowCtx = FlowContextHandler.Create(unexpectedExceptionHandler);

            var cliResult = Utils.IfElse(() => UseCustomHelpRenderer,
                () =>
                {
                    var parser = new Parser(settings => settings.HelpWriter = null);
                    var result = parser.ParseArguments(args, Cli.VerbHandlers.GetVerbTypes(true, false).ToArray());
                    return result.WithNotParsed(errors => Cli.HelpRenderer.DisplayHelp(result, errors, logger));
                },
                () => Parser.Default.ParseArguments(args, Cli.VerbHandlers.GetVerbTypes(true, false).ToArray()));

            var handlers = new Cli.VerbHandlers();
            
            handlers.Register((Cli.VdiskProbe opts) =>
            {
                var recursiveHandler =
                    DiskProbe.GetWalkFsRecursive((_, f) => logger.Log("/" + f.FormatDuPath()));

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

            handlers.Register((Cli.LoopToVhd rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill, opts.NoBoot));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToLoop rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ImgToVhd rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToImg rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.CreateDiffVhd rawOpts) =>
            {
                using var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedTargetAvailable(opts => opts.Child, logger)
                    .WithCheckedSourceExists(opts => opts.Parent, logger)
                    .WithCheckedDiskType(opts => opts.Vhdx ? "VHDX" : "VHD", opts => opts.Parent, opts => opts.Verbose, logger)
                    .WithCheckedTargetExtension(opts => opts.Child, opts => opts.Vhdx ? ".vhdx" : ".vhd")
                    .WithCheckedTargetExtension(opts => opts.Parent, opts => opts.Vhdx ? ".vhdx" : ".vhd")
                    .BindConcat(opts => DiskImage.Util.CreateDiffVdisk(opts.Child, opts.Parent, logger, opts.Vhdx), (opts, disk) => new {opts, disk})
                    .MapDispose(state => state.opts, state => state.disk)
                    .SideEffect(opts => Utils.IfElse(
                        () => opts.Vhdx, 
                        () => new DiskProbe(opts.Child, logger).Probe(), 
                        () => Util.OutputDiagnosticInfo(opts.Child, logger)))
                    .LogOk(logger, "Done.");

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });

            handlers.Register(MakeCreateVdiskHandler<CreateFixedVhd>(logger, flowCtx));
            handlers.Register(MakeCreateVdiskHandler<CreateDynamicVhd>(logger, flowCtx));
            
            handlers.Register((Cli.MergeVhd rawOpts) =>
            {
                return Flows.Val(rawOpts)
                    .Bind(opts => DiskImage.Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger, opts.ZeroFill))
                    .MapDispose(_ => None.Value)
                    .LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ExtractFile rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedSourceExists(x => x.Source, logger)
                    .WithCheckedTargetAvailable(x => x.Target, logger)
                    .WithCheckedStreamBoundaries(x => x.Source, x => x.Offset, x => x.Length, logger)
                    .SideEffect(opts => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.DiagVhd rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
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

                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedSize(x => x.Size)
                    .WithCheckedSourceExists(x => x.Target, logger)
                    .Map(parseSize)
                    .Bind(x => Util.ResizeFixedVhd(x.opts.Target, x.size, logger, rawOpts.ZeroFill));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdToVhdx rawOpts) =>
            {
                var fixedValue = FixCommandLineParserNullable(x => x.Fixed, ["-f", "--fixed"], rawOpts, args, true);
                
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertVhdToVhdx(opts.Source, opts.Target, logger, opts.Verbose, fixedValue, opts.ZeroFill));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.VhdxToVhd rawOpts) =>
            {
                var fixedValue = FixCommandLineParserNullable(x => x.Fixed, ["-f", "--fixed"], rawOpts, args, true);
                
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertVhdxToVhd(opts.Source, opts.Target, logger, opts.Verbose, fixedValue, opts.ZeroFill));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.GenVmdkWrapper rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .WithCheckedDiskType(_ => "RAW", opts => opts.Source, _ => false, logger)
                    .MapOr(opts => new { opts, vmdk = DiskImage.Vmdk.VmdkFlatWrapper.Create(opts.Source, logger) }, "Failed to create VMDK wrapper")
                    .LogIf(logger, state => !state.opts.Silent, new Rule("[green]Resulting VMDK[/]").LeftJustified())
                    .LogOk(logger, state => state.vmdk?.ToString().EscapeMarkup() ?? "")
                    .LogIf(logger, state => !state.opts.Silent, new Rule("[green]End of VMDK[/]").LeftJustified())
                    .LogIf(logger, state => !state.opts.Silent, "Put that into a file, place it into the same folder as the source image and you're good to go");

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.Qcow2ToRaw rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Bind(opts => DiskImageConverter.ConvertQcow2ToRaw(opts.Source, opts.Target, logger, opts.Verbose));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.ProbeBcd rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .SideEffect(opts => BCD.Util.ProbeBcd(opts.Source, opts.Verbose, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVdisk rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .WithCheckedSourceExists(opts => opts.Source, logger)
                    .SideEffect(opts => Commander.FsCommander.OpenDiskImage(opts.Source, logger));

                return result.LogErrorIfAny(logger) ? 1 : 0;
            });
            
            handlers.Register((Cli.BrowseVolumes rawOpts) =>
            {
                if (rawOpts.List)
                {
                    var result = Flows.Val(rawOpts)
                        .WithHandler(flowCtx)
                        .WithSuperUserRights()
                        .Bind(opts => DiskImage.Factory.MakeFsListFromAvailableVolumes(logger, opts.TreeOutput, opts.Humanize));

                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
                else
                {
                    var result = Flows.Val(rawOpts)
                        .WithHandler(flowCtx)
                        .WithSuperUserRights()
                        .Bind(_ => Commander.FsCommander.OpenLocalFs(logger));

                    return result.LogErrorIfAny(logger) ? 1 : 0;
                }
            });

            handlers.Register((OutMarkdownHelp _) =>
            {
                var types = VerbHandlers.GetVerbTypes(false, true)
                    .Select(t => t.GetCustomAttribute<VerbAttribute>())
                    .Where(t => t is { Hidden: false })
                    .Select(v => new
                    {
                        VerbName = v!.Name,
                        v.HelpText,
                    });

                Console.WriteLine(Text.MarkdownGenerator.ToMarkdownTable(types, logger));

                return 0;
            });

            handlers.Register((ResizeFileUnsafe rawOpts) =>
            {
                var result = Flows.Val(rawOpts)
                    .WithHandler(flowCtx)
                    .Check(opts => Fs.Util.HandleResizeFile(opts.Target, opts.Size, opts.ZeroFill, logger), _ => "Failed to resize file");

                return result.IsVal ? 0 : 1;
            });
            
#if DEBUG
            handlers.Register((DebugRepl _) =>
            {
                Dbg.CSharpRepl.EntryPoint();

                return 0;
            });

            handlers.Register([SuppressMessage("ReSharper", "ConvertToLambdaExpression")](Notepad rawOpts) =>
            {
                using var result = Flows.Val(rawOpts).Map(_ => None.Value);

                return 0;
            });
#endif

            Interop.Common.RegisterPlatformSpecificVerbs(handlers, logger, flowCtx);
            
            int exitCode = handlers.HandleParserResult(cliResult);
            
            Console.Out.Flush();
            Console.Error.Flush();

            return exitCode;
        }
        
        
    }
}
