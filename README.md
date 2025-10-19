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

|Command           |Description                                                                                      |
|------------------|-------------------------------------------------------------------------------------------------|
|probe-vdisk       |Probe disk image, try to guess the format, existing partitions and filesystems                   |
|ls-vdisk          |Try to list specific directory in all filesystems that were found                                |
|cat-vdisk         |Try to cat specific file in all filesystems that were found                                      |
|conv-loop-to-vhd  |Wrap raw filesystem loop image into GPT VHD with prepended EFI boot partition                    |
|conv-vhd-to-loop  |Unwrap VHD and create raw filesystem loop                                                        |
|conv-img-to-vhd   |Append VHD footer to RAW image file (in-place)                                                   |
|conv-vhd-to-img   |Delete VHD footer from image, effectively converting it to RAW image (in-place)                  |
|create-diff-vhd   |Create differencing VHD image                                                                    |
|create-fixed-vhd  |Create fixed size VHD image                                                                      |
|merge-vhd         |Merge differencing VHD into parent. Only fixed parent and a single direct child pair is supported|
|create-dynamic-vhd|Create dynamic VHD image                                                                         |
|help              |Display more information on a specific command.                                                  |
|version           |Display version information.                                                                     |

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
* [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32)
* [xUnit](https://xunit.net/?tabs=cs)