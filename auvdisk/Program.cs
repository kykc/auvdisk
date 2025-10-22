using auvdisk.Extensions;
using CommandLine;
using auvdisk.DiskImage;

namespace auvdisk
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class Program
    {
        static int Main(string[] args)
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();
            var cliResult = Parser.Default.ParseArguments<
                Cli.VdiskProbe, Cli.VdiskList, Cli.VdiskCat, Cli.LoopToVhd, 
                Cli.VhdToLoop, Cli.ImgToVhd, Cli.VhdToImg, Cli.CreateDiffVhd, 
                Cli.CreateFixedVhd, Cli.MergeVhd, Cli.CreateDynamicVhd, Cli.ExtractFile,
                Cli.DiagVhd>(args);

            var logger = new Log.Logger();
            var legacyLogger = logger.ToAction();

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
                    DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill);

                    return 0;
                },
                (Cli.VhdToLoop opts) =>
                {
                    DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex);

                    return 0;
                },
                (Cli.ImgToVhd opts) =>
                {
                    DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose);

                    return 0;
                },
                (Cli.VhdToImg opts) =>
                {
                    DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose);

                    return 0;
                },
                (Cli.CreateDiffVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateDifferentialVhd(opts.Parent, opts.Child, logger);
                        DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Child, logger);
                        new DiskProbe(opts.Child, logger).Probe();
                        logger.Log("Done!");
                    };

                    action
                        .WithCheckedDiskType("VHD", opts.Parent, logger, opts.Verbose)
                        .WithCheckedSourceExists(opts.Parent, logger)
                        .WithCheckedTargetAvailable(opts.Child, logger)();

                    return 0;
                },
                (Cli.CreateFixedVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateFixedVhd(opts.Target, opts.Size, logger, opts.ZeroFill);
                    };

                    action
                            .WithCheckedTargetAvailable(opts.Target, logger)();

                    return 0;
                },
                (Cli.MergeVhd opts) =>
                {
                    DiskImage.Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger);

                    return 0;
                },
                (Cli.CreateDynamicVhd opts) =>
                {
                    var action = () =>
                    {
                        DiskImage.Vhd.Util.CreateDynamicVhd(opts.Target, opts.Size, logger);
                        DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Target, logger);
                        new DiskProbe(opts.Target, logger).Probe();
                        logger.Log("Done!");
                    };
                    
                    action.WithCheckedTargetAvailable(opts.Target, logger)();
                    
                    return 0;
                },
                (Cli.ExtractFile opts) =>
                {
                    var action = () => Fs.Util.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length);
                    
                    action
                        .WithCheckedStreamBoundaries(opts.Source, opts.Offset, opts.Length, logger)
                        .WithCheckedTargetAvailable(opts.Target, logger)
                        .WithCheckedSourceExists(opts.Source, logger)();

                    return 0;
                },
                (Cli.DiagVhd opts) =>
                {
                    var action = () => DiskImage.Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);

                    action
                        .WithCheckedSourceExists(opts.Source, logger)();
                    
                    return 0;
                },
                _ => 1
            );

            return exitCode;
        }
    }
}
