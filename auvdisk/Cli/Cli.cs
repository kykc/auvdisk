using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk.Cli
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public class NotSupportedAttribute(bool notSupported) : Attribute
    {
        public bool NotSupported { get; } = notSupported;
        public bool IsSupported => !NotSupported;
    }
    
    class VerbHandlers
    {
        private readonly Dictionary<Type, object> _verbHandlers = new();

        public VerbHandlers Register<T>(Func<T, int> handler)
        {
            _verbHandlers.Add(typeof(T), handler);

            return this;
        }

        public int HandleParserResult(ParserResult<object> result)
        {
            if (result.Value != null)
            {
                return (int)(_verbHandlers[result.Value.GetType()] as Delegate)!.DynamicInvoke(result.Value)!;
            }
            else
            {
                return 2;
            }
        }
        
        public static IEnumerable<Type> GetVerbTypes(bool includeHidden = false, bool includeUnsupported = false)
        {
            return Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsDefined(typeof(VerbAttribute), true))
                .Where(t => 
                    (!(t.GetCustomAttribute<NotSupportedAttribute>()?.NotSupported ?? false) || includeUnsupported) && 
                    (!(t.GetCustomAttribute<VerbAttribute>()?.Hidden ?? false) || includeHidden));
        }
    }
    
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("browse-vdisk", HelpText = "View virtual disk contents in a simple mc/nc-like file explorer")]
    class BrowseVdisk
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("probe-vdisk", HelpText = "Probe disk image, try to guess the format, existing partitions and filesystems ")]
    class VdiskProbe
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";

        [Option('v', "verbose", Required = false, Default = false, HelpText = "Enable verbose output")]
        public bool Verbose { get; set; } = false;
        [Option('p', "part-idx", Required = false, Default = -1, HelpText = "Execute FS handler only on partition with specified index")]
        public int PartIdx { get; set; } = -1;
        [Option('r', "recursive", Required = false, Default = false, HelpText = "List filesystem(s) recursively")]
        public bool Recursive { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("browse-volumes",
        HelpText = "Browse volumes available on the local system, avoiding using system FS drivers")]
    class BrowseVolumes
    {
        [Option('l', "list", Required = false, HelpText = "List the volumes instead of browsing")]
        public bool List { get; set; } = false;

        [Option('t', "tree", Required = false, Default = false, HelpText = "Tree output")]
        public bool TreeOutput { get; set; } = false;
        [Option('h', "humanize", Required = false, Default = false, HelpText = "Humanize large byte lengths to be human-readable")]
        public bool Humanize { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("probe-bcd", HelpText = "Probe Windows BCD database and output records. Very basic, doesn't locate devices. Might be useful on Linux as nothing else is available there.")]
    class ProbeBcd
    {
        [Option('s', "source", Required = true, HelpText = "Path to BCD file")]
        public string Source { get; set; } = "";

        [Option('v', "verbose", Required = false, Default = false, HelpText = "Enable verbose output")]
        public bool Verbose { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("ls-vdisk", HelpText = "Try to list specific directory in all filesystems that were found")]
    class VdiskList
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target search path")]
        public string Target { get; set; } = "";
        [Option('p', "part-idx", Required = false, Default = -1, HelpText = "Execute FS handler only on partition with specified index")]
        public int PartIdx { get; set; } = -1;
        [Option("silent", Required = false, Default = false, HelpText = "Suppress all output except FS handler")]
        public bool Silent { get; set; } = false;
        [Option("filter", Required = false, Default = "", HelpText = "Regex output filtering")]
        public string Filter { get; set; } = "";
        [Option('r', "recursive", Required = false, Default = false, HelpText = "List filesystem(s) recursively")]
        public bool Recursive { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("cat-vdisk", HelpText = "Try to cat specific file in all filesystems that were found")]
    class VdiskCat
    {
        [Option('s', "source", Required = true, HelpText = "Path to vdisk image file")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
        [Option('p', "part-idx", Required = false, Default = -1, HelpText = "Execute FS handler only on partition with specified index")]
        public int PartIdx { get; set; } = -1;
        [Option("silent", Required = false, Default = false, HelpText = "Suppress all output except FS handler")]
        public bool Silent { get; set; } = false;
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
        [Option("no-boot", Required = false, Default = false, HelpText = "Do not create EFI Boot partition")]
        public bool NoBoot { get; set; } = false;
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
    class CreateFixedVhd : CreateVdisk
    {
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Explicitly zero-fill created virtual disk. May help avoiding creation of sparse file")]
        public bool ZeroFill { get; set; } = false;
        [Option("vhdx",  Required = false, Default = false, HelpText = "Create VHDx instead of VHD")]
        public bool Vhdx { get; set; } = false;

        public override bool ZeroFillRequired() => ZeroFill;
        public override bool IsDynamic() => false;
        public override bool IsVhdx() => Vhdx;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("create-dynamic-vhd", HelpText = "Create dynamic VHD image")]
    class CreateDynamicVhd : CreateVdisk
    {
        [Option("vhdx",  Required = false, Default = false, HelpText = "Create VHDx instead of VHD")]
        public bool Vhdx { get; set; } = false;
        
        public override bool ZeroFillRequired() => false;
        public override bool IsDynamic() => true;
        public override bool IsVhdx() => Vhdx;
    }
    
    abstract class CreateVdisk
    {
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
        [Option('s', "size", Required = true, HelpText = "Target virtual disk size in bytes")]
        public string Size { get; set; } = "";
        [Option("partition", Required = false, Default = "", HelpText = "Initialize target Virtual Disk with GPT partition table and create partition layout. Example: `512MiB, 0`")]
        public string Partition { get; set; } = "";
        [Option('b', "boot", Required = false, Default = false, HelpText = "Mark first partition as boot with GPT Type GUID (valid only with --partition)")]
        public bool Boot { get; set; } = false;


        public abstract bool ZeroFillRequired();
        public abstract bool IsDynamic();
        public abstract bool IsVhdx();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("merge-vhd", HelpText = "Merge differencing VHD into parent. Supports both in-place and new image/branch modes.")]
    class MergeVhd
    {
        [Option('p', "parent", Required = true, HelpText = "Parent VHD image")]
        public string Parent { get; set; } = "";
        [Option('c', "child", Required = true, HelpText = "Child differencing VHD image")]
        public string Child { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target VHD path")]
        public string Target { get; set; } = "";
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Explicitly zero-fill created virtual disk. Effective only if creating new fixed image.")]
        public bool ZeroFill { get; set; } = false;
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
        public string Size { get; set; } = "";
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Zero-fill added space in resized VHD. May help avoiding creation of sparse file")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("check-is-sparse", HelpText = "Check if file is a sparse file (Windows and NTFS only)")]
    [NotSupported(!Program.IsWindows)]
    class CheckIsSparse
    {
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhd-to-vhdx", HelpText = "Convert any VHD image to VHDx image")]
    class VhdToVhdx
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;

        [Option('f', "fixed", Required = false, HelpText = "Force full disk preallocation (fixed VHDx)")]
        public bool? Fixed { get; set; }
        [Option('z',  "zero", Required = false, Default = false, HelpText = "Force zero-fill (only valid with --fixed)")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("conv-vhdx-to-vhd", HelpText = "Convert any VHDX image to VHD image")]
    class VhdxToVhd
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option('t', "target", Required = true, HelpText = "Target imagefile path")]
        public string Target { get; set; } = "";
        [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose output from disk prober")]
        public bool Verbose { get; set; } = false;
        [Option('f', "fixed", Required = false, HelpText = "Force full disk preallocation (fixed VHDx)")]
        public bool? Fixed { get; set;}
        [Option('z',  "zero", Required = false, Default = false, HelpText = "Force zero-fill (only valid with --fixed)")]
        public bool ZeroFill { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("gen-vmdk-wrapper", HelpText = "Generate VMDK wrapper for a RAW image")]
    class GenVmdkWrapper
    {
        [Option('s', "source", Required = true, HelpText = "Source imagefile path")]
        public string Source { get; set; } = "";
        [Option("silent", Required = false, Default = false, HelpText = "Suppress all output except VMDK contents")]
        public bool Silent { get; set; } = false;
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

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("change-partition-type", HelpText = "Generate diskpart command for changing GPT partition type (Windows only)")]
    [NotSupported(!Program.IsWindows)]
    class ChangePartitionType
    {
        [Option('d', "disk", Required = false, HelpText = "Disk number")]
        public int DiskNumber { get; set; } = 0;
        [Option('p', "partition", Required = false, HelpText = "Partition number")]
        public int PartitionNumber { get; set; } = 0;
        [Option('t', "type", Required = false, HelpText = "Partition type")]
        public string PartitionType { get; set; } = "";
        [Option('y', "yes", Required = false, Default = false, HelpText = "Execute generated script w/o asking any questions")]
        public bool Yes { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("out-markdown-help", HelpText = "Output markdown help (for internal use only)", Hidden = true)]
    class OutMarkdownHelp
    {
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("resize-file-unsafe", HelpText = "Resize file (for internal use only)", Hidden = true)]
    class ResizeFileUnsafe
    {
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
        [Option('s', "size",  Required = true, HelpText = "Size of the file")]
        public ulong Size { get; set; } = 0;
        [Option('z', "zero-fill", Required = false, Default = false, HelpText = "Force zero-fill")]
        public bool ZeroFill { get; set; } = false;
    }
    
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("clone-volume-to-vhd", HelpText = "Clone live mounted volume into VHD using Volume Shadow Copy (Windows only)")]
    [NotSupported(!Program.IsWindows)]
    public class CloneLiveVolumeToVhd
    {
        [Option('t', "target", Required = true, HelpText = "Target file path")]
        public string Target { get; set; } = "";
        [Option('s', "source", Required = true, HelpText = "Source volume path (letter)")]
        public string Source { get; set; } = "";
        [Option('f', "fixed", Required = false, Default = false, HelpText = "Force full disk preallocation (fixed VHD)")]
        public bool Fixed { get; set;} = false;
        [Option('z',  "zero", Required = false, Default = false, HelpText = "Force zero-fill (only valid with --fixed)")]
        public bool ZeroFill { get; set; } = false;
        [Option('b', "bootable", Required = false, HelpText = "Make VHD bootable using bcdboot")]
        public bool Bootable { get; set; } = false;
        [Option("vhdx", Required = false, Default = false, HelpText = "Create VHDx image instead of VHD")]
        public bool Vhdx { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("mount-vhd-x", HelpText = "Mount VHD/VHDx image (Windows only)")]
    [NotSupported(!Program.IsWindows)]
    public class MountVhdX
    {
        [Option('t', "target", Required = true, HelpText = "Target image path")]
        public string Target { get; set; } = "";
        [Option('d', "dismount", Required = false, Default = false, HelpText = "Dismount disk image")]
        public bool Dismount { get; set; } = false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("mount-volume", HelpText = "Assign letter to volume (Windows only)")]
    [NotSupported(!Program.IsWindows)]
    public class AssignVolumeLetter
    {
        [Option('v', "volume", Required = true, HelpText = "Path volume (example: \\\\?\\Volume{0deefc43-02e6-40d8-8978-e4874fb4b405}\\). Can be found using `browse-volumes --list`")]
        public string Volume { get; set; } = "";
        [Option('l', "letter", Required = true, HelpText = "Target drive letter")]
        public string Letter { get; set; } = "";
    }
    
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("unmount-volume", HelpText = "Remove drive letter association from volume (Windows only)")]
    [NotSupported(!Program.IsWindows)]
    public class UnassignVolumeLetter
    {
        [Option('l', "letter", Required = true, HelpText = "Target drive letter")]
        public string Letter { get; set; } = "";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("debug-repl", HelpText = "Debug C# REPL", Hidden = true)]
    [NotSupported(!Program.IsDebug)]
    public class DebugRepl
    {
    }
    
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [Verb("check-ntfs-last-cluster", HelpText = "Check NTFS Last Cluster", Hidden = true)]
    [NotSupported(!Program.IsWindows)]
    public class CheckNtfsLastCluster
    {
        [Option('v', "volume", Required = true, HelpText = "Path volume (example: \\\\?\\Volume{0deefc43-02e6-40d8-8978-e4874fb4b405}\\ or \\\\.\\X:). Can be found using `browse-volumes --list`")]
        public string Volume { get; set; } = "";
        [Option('x', "extended-ioctl", Required = false, Default = false, HelpText = "Grant FSCTL_ALLOW_EXTENDED_DASD_IO on volume opening")]
        public bool GrantExtendedIoctl { get; set; } = false;
    }
}
