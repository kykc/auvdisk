using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk.Cli
{
    [Verb("vdisk-probe", HelpText = "Probe disk image, try to guess the format, existing partitions and filesystems ")]
    class VdiskProbe
    {
        [Option('o', "offset", Default = 0, HelpText = "Skip number of files from the beginning of the file")]
        public long Offset { get; set; } = 0;
        [Option('t', "trim", Default = 0, HelpText = "Skip number of files from the end of the file")]
        public long Trim { get; set; } = 0;
        [Option('p', "path", Required = true, HelpText = "Path to vdisk image file")]
        public string Path { get; set; } = "";
    }

    [Verb("vdisk-list", HelpText = "Try to list specific directory in all filesystems that were found")]
    class VdiskList
    {
        [Option('o', "offset", Default = 0, HelpText = "Skip number of files from the beginning of the file")]
        public long Offset { get; set; } = 0;
        [Option('t', "trim", Default = 0, HelpText = "Skip number of files from the end of the file")]
        public long Trim { get; set; } = 0;
        [Option('p', "path", Required = true, HelpText = "Path to vdisk image file")]
        public string Path { get; set; } = "";
        [Option("target", Required = true, HelpText = "Target search path")]
        public string Target { get; set; } = "";
    }

    [Verb("vdisk-cat", HelpText = "Try to cat specific file in all filesystems that were found")]
    class VdiskCat
    {
        [Option('o', "offset", Default = 0, HelpText = "Skip number of files from the beginning of the file")]
        public long Offset { get; set; } = 0;
        [Option('t', "trim", Default = 0, HelpText = "Skip number of files from the end of the file")]
        public long Trim { get; set; } = 0;
        [Option('p', "path", Required = true, HelpText = "Path to vdisk image file")]
        public string Path { get; set; } = "";
        [Option("target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
    }

    [Verb("loop-to-vhd", HelpText = "Wrap raw filesystem loop image into GPT VHD with prepended EFI boot partition")]
    class LoopToVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
    }

    [Verb("vhd-to-loop", HelpText = "Unwrap VHD and create raw filesystem loop")]
    class VhdToLoop
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
    }

    [Verb("img-to-vhd", HelpText = "Append VHD footer to RAW image file")]
    class ImgToVhd
    {
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
    }

    [Verb("vhd-to-img", HelpText = "Delete VHD footer from image, effectively converting it to RAW image")]
    class VhdToImg
    {
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
    }
}
