using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk.Cli
{
    static class Extensions
    {
        public static ParserResult<object> ParseArguments<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>(this Parser parser, IEnumerable<string> args)
        {
            if (parser == null) throw new ArgumentNullException("parser");

            return parser.ParseArguments(args, new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8),
                typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15), typeof(T16), typeof(T17), typeof(T18), typeof(T19) });
        }

        public static TResult MapResult<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, TResult>(this ParserResult<object> result,
            Func<T1, TResult> parsedFunc1,
            Func<T2, TResult> parsedFunc2,
            Func<T3, TResult> parsedFunc3,
            Func<T4, TResult> parsedFunc4,
            Func<T5, TResult> parsedFunc5,
            Func<T6, TResult> parsedFunc6,
            Func<T7, TResult> parsedFunc7,
            Func<T8, TResult> parsedFunc8,
            Func<T9, TResult> parsedFunc9,
            Func<T10, TResult> parsedFunc10,
            Func<T11, TResult> parsedFunc11,
            Func<T12, TResult> parsedFunc12,
            Func<T13, TResult> parsedFunc13,
            Func<T14, TResult> parsedFunc14,
            Func<T15, TResult> parsedFunc15,
            Func<T16, TResult> parsedFunc16,
            Func<T17, TResult> parsedFunc17,
            Func<T18, TResult> parsedFunc18,
            Func<T19, TResult> parsedFunc19,
            Func<IEnumerable<Error>, TResult> notParsedFunc)
        {
            var parsed = result as Parsed<object>;
            if (parsed != null)
            {
                if (parsed.Value is T1)
                {
                    return parsedFunc1((T1)parsed.Value);
                }
                if (parsed.Value is T2)
                {
                    return parsedFunc2((T2)parsed.Value);
                }
                if (parsed.Value is T3)
                {
                    return parsedFunc3((T3)parsed.Value);
                }
                if (parsed.Value is T4)
                {
                    return parsedFunc4((T4)parsed.Value);
                }
                if (parsed.Value is T5)
                {
                    return parsedFunc5((T5)parsed.Value);
                }
                if (parsed.Value is T6)
                {
                    return parsedFunc6((T6)parsed.Value);
                }
                if (parsed.Value is T7)
                {
                    return parsedFunc7((T7)parsed.Value);
                }
                if (parsed.Value is T8)
                {
                    return parsedFunc8((T8)parsed.Value);
                }
                if (parsed.Value is T9)
                {
                    return parsedFunc9((T9)parsed.Value);
                }
                if (parsed.Value is T10)
                {
                    return parsedFunc10((T10)parsed.Value);
                }
                if (parsed.Value is T11)
                {
                    return parsedFunc11((T11)parsed.Value);
                }
                if (parsed.Value is T12)
                {
                    return parsedFunc12((T12)parsed.Value);
                }
                if (parsed.Value is T13)
                {
                    return parsedFunc13((T13)parsed.Value);
                }
                if (parsed.Value is T14)
                {
                    return parsedFunc14((T14)parsed.Value);
                }
                if (parsed.Value is T15)
                {
                    return parsedFunc15((T15)parsed.Value);
                }
                if (parsed.Value is T16)
                {
                    return parsedFunc16((T16)parsed.Value);
                }
                if (parsed.Value is T17)
                {
                    return parsedFunc17((T17)parsed.Value);
                }
                if (parsed.Value is T18)
                {
                    return parsedFunc18((T18)parsed.Value);
                }
                if (parsed.Value is T19)
                {
                    return parsedFunc19((T19)parsed.Value);
                }
                throw new InvalidOperationException();
            }
            return notParsedFunc(((NotParsed<object>)result).Errors);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("probe-vdisk", HelpText = "Probe disk image, try to guess the format, existing partitions and filesystems ")]
    class VdiskProbe
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";

        [Option('v', "verbose", Required = false, Default = false, HelpText = "Enable verbose output")]
        public bool Verbose { get; set; } = false;
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

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("resize-fixed-vhd", HelpText = "Resize fixed VHD image")]
    class ResizeFixedVhd
    {
        [Option('t', "target", Required = true, HelpText = "Target VHD path")]
        public string Target { get; set; } = "";
        [Option('s', "size", Required = true, HelpText = "Target VHD size in bytes. Actual file will be 512 bytes longer, as it needs to contain VHD footer. Needs to be > current size")]
        public ulong Size { get; set; } = 0;
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Zero-fill added space in resized VHD. May help avoiding creation of sparse file")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("check-is-sparse", HelpText = "Check if file is a sparse file (Windows and NTFS only)")]
    class CheckIsSparse
    {
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhd-to-vhdx", HelpText = "Convert any VHD image to fixed VHDX image")]
    class VhdToVhdx
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhdx-to-vhd", HelpText = "Convert any VHDX image to fixed VHD image")]
    class VhdxToVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("gen-vmdk-wrapper", HelpText = "Generate VMDK wrapper for a RAW image")]
    class GenVmdkWrapper
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-qcow-to-raw", HelpText = "Convert qcow2 image to RAW")]
    class Qcow2ToRaw
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
    }
}
