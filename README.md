# Auvdisk - cli utility to manipulate (some) of the disk image formats

## A word of WARNING

Usage of this utility can lead to data loss if not used carefully. I tried to keep the code clean and test it
the best I could, but people make mistakes and I am no exception. ALWAYS BACKUP before using it, this code
is shared as is, no warranties whatsoever.

## Motivation

Over the years I continuously had a feeling that there's a tool missing in my toolbox when tinkering with
various OS and their images, jumping from environment to environment. Finally, I decided to build this
tool myself.

## Building

### Prerequisites

* [.net 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Building

Just run `dotnet build` in your favorite shell. You can also easily build `deb` and `choco` (and probably other)
packages directly from this repo (see `.gitea/workflows` for some examples on package building)

## Functionality 

`auvdisk --help` output:

| Verb                  | Description                                                                                                                                     |
|-----------------------|-------------------------------------------------------------------------------------------------------------------------------------------------|
| browse-vdisk          | View virtual disk contents in a simple mc/nc-like file explorer                                                                                 |
| probe-vdisk           | Probe disk image, try to guess the format, existing partitions and filesystems                                                                  |
| browse-volumes        | Browse volumes available on the local system, avoiding using system FS drivers                                                                  |
| probe-bcd             | Probe Windows BCD database and output records. Very basic, doesn't locate devices. Might be useful on Linux as nothing else is available there. |
| ls-vdisk              | Try to list specific directory in all filesystems that were found                                                                               |
| cat-vdisk             | Try to cat specific file in all filesystems that were found                                                                                     |
| conv-loop-to-vhd      | Wrap raw filesystem loop image into GPT VHD with prepended EFI boot partition, formatted into FAT32                                             |
| conv-vhd-to-loop      | Extract raw filesystem image from one of the partitions present in the source VHD image                                                         |
| conv-img-to-vhd       | Append VHD footer to RAW image file (in-place)                                                                                                  |
| conv-vhd-to-img       | Delete VHD footer from image, effectively converting it to RAW image (in-place)                                                                 |
| create-diff-vhd       | Create differencing VHD image                                                                                                                   |
| create-fixed-vhd      | Create fixed size VHD image                                                                                                                     |
| create-dynamic-vhd    | Create dynamic VHD image                                                                                                                        |
| merge-vhd             | Merge differencing VHD into parent. Supports both in-place and new image/branch modes.                                                          |
| extract-file          | Extract file using offset and length in bytes                                                                                                   |
| diag-vhd              | Output VHD diagnostics info                                                                                                                     |
| resize-fixed-vhd      | Resize fixed VHD image                                                                                                                          |
| check-is-sparse       | Check if file is a sparse file (Windows and NTFS only)                                                                                          |
| conv-vhd-to-vhdx      | Convert any VHD image to VHDx image                                                                                                             |
| conv-vhdx-to-vhd      | Convert any VHDX image to VHD image                                                                                                             |
| gen-vmdk-wrapper      | Generate VMDK wrapper for a RAW image                                                                                                           |
| conv-qcow-to-raw      | Convert qcow2 image to RAW                                                                                                                      |
| change-partition-type | Generate diskpart command for changing GPT partition type (Windows only)                                                                        |
| clone-volume-to-vhd   | Clone live mounted volume into VHD using Volume Shadow Copy (Windows only)                                                                      |
| mount-vhd-x           | Mount VHD/VHDx image (Windows only)                                                                                                             |
| mount-volume          | Assign letter to volume (Windows only)                                                                                                          |
| unmount-volume        | Remove drive letter association from volume (Windows only)                                                                                      |

## Unit tests

Unit tests are located in a separate `auvdisk.tests` project. As I didn't decouple my code from direct calls
to the FS APIs the only way to write tests was to include some binary image blobs as testing data. If you're
allergic to binary blobs of unknown origin you should probably avoid building the tests. But I swear that those
are harmless test images without any shady xz-style code injections.

To run the tests simply run `dotnet run` from the tests project directory or look into xUnit documentation on how to
integrate them with your favorite IDE.

## Used libraries

Many thanks to the original authors of `DiskAccessLibrary` and `DiscUtils` (with all of its forks), they actually did 90% of 
the work that was needed here. The only thing I had to write "from scratch" was VHD merging.

* [DiscUtils](https://github.com/LTRData/DiscUtils)
* [DiskAccessLibrary](https://github.com/TalAloni/DiskAccessLibrary)
* [CommandLineParser](https://github.com/commandlineparser/commandline)
* [DotNext](https://dotnet.github.io/dotNext/)
* [Spectre.Console](https://spectreconsole.net/)
* [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)
* [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32)
* [xUnit](https://xunit.net/?tabs=cs)