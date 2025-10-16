using CommandLine;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using DiscUtils.Vhd;
using System;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;

namespace auvdisk
{
    internal class Program
    {
        public static ulong LbaSize = 512;

        static int Main(string[] args)
        {
            var cliResult = Parser.Default.ParseArguments<Cli.VdiskProbe, Cli.VdiskList, Cli.VdiskCat, Cli.LoopToVhd, Cli.VhdToLoop, Cli.ImgToVhd, Cli.VhdToImg>(args);
            var logger = (string s) => Console.WriteLine(s);

            var exitCode = cliResult.MapResult(
                (Cli.VdiskProbe opts) =>
                {
                    var probe = new DiskProbe(opts.Path, opts.Offset, opts.Trim, null, logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskList opts) =>
                {
                    var probe = new DiskProbe(opts.Path, opts.Offset, opts.Trim, DiskProbe.GetListArbitraryDir(opts.Target, logger), logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.VdiskCat opts) =>
                {
                    var probe = new DiskProbe(opts.Path, opts.Offset, opts.Trim, DiskProbe.GetCatArbitraryFile(opts.Target, logger), logger);
                    probe.Probe();

                    return 0;
                },
                (Cli.LoopToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertLoopToVhd(opts.Source, opts.Target, logger, opts.Verbose);

                    return 0;
                },
                (Cli.VhdToLoop opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToLoop(opts.Source, opts.Target, logger, opts.Verbose);

                    return 0;
                },
                (Cli.ImgToVhd opts) =>
                {
                    Convert.DiskImageConverter.ConvertImgToVhd(opts.Target, logger, opts.Verbose);

                    return 0;
                },
                (Cli.VhdToImg opts) =>
                {
                    Convert.DiskImageConverter.ConvertVhdToImg(opts.Target, logger, opts.Verbose);

                    return 0;
                },
                _ => 1
            );

            return exitCode;
        }
    }
}
