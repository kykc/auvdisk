using auvdisk.Extensions;
using CommandLine;
using System;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;
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
                Cli.CreateFixedVhd, Cli.MergeVhd, Cli.CreateDynamicVhd, Cli.ExtractFile>(args);
            var logger = (string s) =>
            {
                if (s.StartsWith("ERROR"))
                {
                    AnsiConsole.MarkupLine($"[red]{s}[/]");
                }
                else if (s.StartsWith("WARNING"))
                {
                    AnsiConsole.MarkupLine($"[yellow]{s}[/]");
                }
                else
                {
                    try
                    {
                        AnsiConsole.MarkupLine(s);
                    }
                    catch (InvalidOperationException)
                    {
                        AnsiConsole.WriteLine(s);
                    }
                }
            };

            var exitCode = cliResult.MapResult(
                (Cli.VdiskProbe opts) =>
                {
                    var probe = new DiskProbe(opts.Source, null, logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskList opts) =>
                {
                    var probe = new DiskProbe(opts.Source, DiskProbe.GetListArbitraryDir(opts.Target, logger), logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskCat opts) =>
                {
                    var probe = new DiskProbe(opts.Source, DiskProbe.GetCatArbitraryFile(opts.Target, logger), logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.LoopToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose, opts.ZeroFill);

                    return 0;
                },
                (Cli.VhdToLoop opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose, opts.PartIndex);

                    return 0;
                },
                (Cli.ImgToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertImgToVhd(opts.Source, logger, opts.Verbose);

                    return 0;
                },
                (Cli.VhdToImg opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToImg(opts.Source, logger, opts.Verbose);

                    return 0;
                },
                (Cli.CreateDiffVhd opts) =>
                {
                    var action = () =>
                    {
                        DiscUtils.Vhd.Disk.InitializeDifferencing(opts.Child, opts.Parent);
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
                        Vhd.Util.CreateFixedVhd(opts.Target, opts.Size, logger, opts.ZeroFill);
                    };

                    action
                            .WithCheckedTargetAvailable(opts.Target, logger)();

                    return 0;
                },
                (Cli.MergeVhd opts) =>
                {
                    Vhd.Merge.PerformMerge(opts.Parent, opts.Child, opts.Target, logger);

                    return 0;
                },
                (Cli.CreateDynamicVhd opts) =>
                {
                    var action = () =>
                    {
                        Vhd.Util.CreateDynamicVhd(opts.Target, opts.Size);
                        logger("Done!");
                    };
                    
                    action.WithCheckedTargetAvailable(opts.Target, logger)();
                    
                    return 0;
                },
                (Cli.ExtractFile opts) =>
                {
                    var action = () => FsUtils.ExtractFileSegment(opts.Source, opts.Target, opts.Offset, opts.Length);
                    
                    action
                        .WithCheckedStreamBoundaries(opts.Source, opts.Offset, opts.Length, logger)
                        .WithCheckedTargetAvailable(opts.Target, logger)
                        .WithCheckedSourceExists(opts.Source, logger)();

                    return 0;
                },
                _ => 1
            );

            return exitCode;
        }
    }
}
