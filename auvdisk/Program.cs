using auvdisk.Extensions;
using CommandLine;
using System;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;
using auvdisk.Log;
using Spectre.Console;

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
            
            var legacyLogger = (string s) =>
            {
                logger.Log(s);
            };

            var exitCode = cliResult.MapResult(
                (Cli.VdiskProbe opts) =>
                {
                    var probe = new DiskProbe(opts.Source, null, legacyLogger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskList opts) =>
                {
                    var probe = new DiskProbe(opts.Source, DiskProbe.GetListArbitraryDir(opts.Target, legacyLogger), legacyLogger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskCat opts) =>
                {
                    var probe = new DiskProbe(opts.Source, DiskProbe.GetCatArbitraryFile(opts.Target, legacyLogger), legacyLogger);
                    probe.Probe();

                    return 0;
                },
                (Cli.LoopToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, legacyLogger, opts.Verbose, opts.ZeroFill);

                    return 0;
                },
                (Cli.VhdToLoop opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, legacyLogger, opts.Verbose, opts.PartIndex);

                    return 0;
                },
                (Cli.ImgToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertImgToVhd(opts.Source, legacyLogger, opts.Verbose);

                    return 0;
                },
                (Cli.VhdToImg opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToImg(opts.Source, legacyLogger, opts.Verbose);

                    return 0;
                },
                (Cli.CreateDiffVhd opts) =>
                {
                    var action = () =>
                    {
                        Vhd.Util.CreateDifferentialVhd(opts.Parent, opts.Child, logger);
                        Vhd.Util.OutputDiagnosticInfo(opts.Child, logger);
                        new DiskProbe(opts.Child, null, legacyLogger).Probe();
                        logger.Log("Done!");
                    };

                    action
                        .WithCheckedDiskType("VHD", opts.Parent, legacyLogger, opts.Verbose)
                        .WithCheckedSourceExists(opts.Parent, legacyLogger)
                        .WithCheckedTargetAvailable(opts.Child, legacyLogger)();

                    return 0;
                },
                (Cli.CreateFixedVhd opts) =>
                {
                    var action = () =>
                    {
                        Vhd.Util.CreateFixedVhd(opts.Target, opts.Size, legacyLogger, opts.ZeroFill);
                    };

                    action
                            .WithCheckedTargetAvailable(opts.Target, legacyLogger)();

                    return 0;
                },
                (Cli.MergeVhd opts) =>
                {
                    Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, legacyLogger);

                    return 0;
                },
                (Cli.CreateDynamicVhd opts) =>
                {
                    var action = () =>
                    {
                        Vhd.Util.CreateDynamicVhd(opts.Target, opts.Size, logger);
                        Vhd.Util.OutputDiagnosticInfo(opts.Target, logger);
                        new DiskProbe(opts.Target, null, legacyLogger).Probe();
                        logger.Log("Done!");
                    };
                    
                    action.WithCheckedTargetAvailable(opts.Target, legacyLogger)();
                    
                    return 0;
                },
                (Cli.ExtractFile opts) =>
                {
                    var action = () => FsUtils.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length);
                    
                    action
                        .WithCheckedStreamBoundaries(opts.Source, opts.Offset, opts.Length, legacyLogger)
                        .WithCheckedTargetAvailable(opts.Target, legacyLogger)
                        .WithCheckedSourceExists(opts.Source, legacyLogger)();

                    return 0;
                },
                (Cli.DiagVhd opts) =>
                {
                    var action = () => Vhd.Util.OutputDiagnosticInfo(opts.Source, logger);

                    action
                        .WithCheckedSourceExists(opts.Source, legacyLogger)();
                    
                    return 0;
                },
                _ => 1
            );

            return exitCode;
        }
    }
}
