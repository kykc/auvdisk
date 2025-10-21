using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk.Cli
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("probe-vdisk", HelpText = "Probe disk image, try to guess the format, existing partitions and filesystems ")]
    class VdiskProbe
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("ls-vdisk", HelpText = "Try to list specific directory in all filesystems that were found")]
    class VdiskList
    {
        [Option("offset", Default = 0, HelpText = "Skip number of bytes from the beginning of the file")]
        public long Offset { get; set; } = 0;
        [Option("trim", Default = 0, HelpText = "Skip number of bytes from the end of the file")]
        public long Trim { get; set; } = 0;
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target search path")]
        public string Target { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("cat-vdisk", HelpText = "Try to cat specific file in all filesystems that were found")]
    class VdiskCat
    {
        [Option("offset", Default = 0, HelpText = "Skip number of bytes from the beginning of the file")]
        public long Offset { get; set; } = 0;
        [Option("trim", Default = 0, HelpText = "Skip number of bytes from the end of the file")]
        public long Trim { get; set; } = 0;
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-loop-to-vhd", HelpText = "Wrap raw filesystem loop image into GPT VHD with prepended EFI boot partition, formatted into FAT32")]
    class LoopToVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Explicitly zero-fill created VHD. May help avoiding creation of sparse file")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhd-to-loop", HelpText = "Extract raw filesystem image from one of the partitions present in the source VHD image")]
    class VhdToLoop
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
        [Option("part-index", Required = false, Default = -1, HelpText = "Partition index. Select largest partition by default")]
        public int PartIndex { get; set; } = -1;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-img-to-vhd", HelpText = "Append VHD footer to RAW image file (in-place)")]
    class ImgToVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source (and target) imagefile path")]
        public string Source { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhd-to-img", HelpText = "Delete VHD footer from image, effectively converting it to RAW image (in-place)")]
    class VhdToImg
    {
        [Option('s', "source", Required = true, HelpText = "Source (and target) imagefile path")]
        public string Source { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("create-diff-vhd", HelpText = "Create differencing VHD image")]
    class CreateDiffVhd
    {
        [Option('p', "parent", Required = true, HelpText = "Parent VHD image")]
        public string Parent { get; set; } = "";
        [Option('c', "child", Required = true, HelpText = "Child differencing VHD image")]
        public string Child { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("create-fixed-vhd", HelpText = "Create fixed size VHD image")]
    class CreateFixedVhd
    {
        [Option('t', "target", Required = true, HelpText = "Target VHD path")]
        public string Target { get; set; } = "";
        [Option('s', "size", Required = true, HelpText = "Target VHD size in bytes. Actual file will be 512 bytes longer, as it needs to contain VHD footer")]
        public ulong Size { get; set; } = 0;
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Explicitly zero-fill created VHD. May help avoiding creation of sparse file")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("create-dynamic-vhd", HelpText = "Create dynamic VHD image")]
    class CreateDynamicVhd
    {
        [Option('t', "target", Required = true, HelpText = "Target VHD path")]
        public string Target { get; set; } = "";
        [Option('s', "size", Required = true, HelpText = "Target VHD size in bytes. Actual file will be 512 bytes longer, as it needs to contain VHD footer")]
        public ulong Size { get; set; } = 0;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("merge-vhd", HelpText = "Merge differencing VHD into parent. Only fixed parent and a single direct child pair is supported")]
    class MergeVhd
    {
        [Option('p', "parent", Required = true, HelpText = "Parent VHD image")]
        public string Parent { get; set; } = "";
        [Option('c', "child", Required = true, HelpText = "Child differencing VHD image")]
        public string Child { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target VHD path")]
        public string Target { get; set; } = "";
    }
    
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("extract-file", HelpText = "Extract file using offset and length in bytes")]
    class ExtractFile
    {
        [Option('s', "source", Required = true, HelpText = "Source file path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
        [Option('o', "offset", Required = true, HelpText = "Skip number of bytes from the beginning of the file")]
        public ulong Offset { get; set; } = 0;

        [Option('l', "length", Required = true, HelpText = "Count of bytes to copy")]
        public ulong Length { get; set; } = 0;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("diag-vhd", HelpText = "Output VHD diagnostics info")]
    class DiagVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source VHD file path")]
        public string Source { get; set; } = "";
    }
}
