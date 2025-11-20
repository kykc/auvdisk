using System.Runtime.InteropServices;
using DiscUtils.Streams;
using DiskAccessLibrary.VHD;
using DiskAccessLibrary;
using Spectre.Console;
using System.Text;
using DiscUtils.Vhd;
using System.Text.RegularExpressions;
using auvdisk.Bytes;
using auvdisk.Cli;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using ZstdSharp.Unsafe;

namespace auvdisk.DiskImage.Vhd
{
    public static class Util
    {
        public const ulong LbaSize = 512;
        
        public static VHDFooter? ReadVhdFooterSafe(string source)
        {
            try
            {
                using var stream = new FileStream(source, FileMode.Open, FileAccess.Read);

                if (stream.Length > (long)LbaSize)
                {
                    byte[] footerBytes = new byte[LbaSize];
                    stream.Seek(-(long)LbaSize, SeekOrigin.End);
                    stream.ReadExactly(footerBytes);

                    var header = new VHDFooter(footerBytes);

                    return header.IsValid ? header : null;
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return null;
        }

        public static Flow<VhdFileInfo> CreateDynamicVhd(string path, ulong size, Log.ILog logger)
        {
            return CreateNonFixedVhd(path, size, null, logger);
        }

        public static Flow<VhdFileInfo> CreateDifferentialVhd(string parent, string child, Log.ILog logger)
        {
            return CreateNonFixedVhd(child, null, parent, logger);
        }

        public static Flow<VhdFileInfo> ResizeFixedVhd(string path, ulong size, Log.ILog logger, bool zeroFill = false)
        {
            if (size % LbaSize > 0)
            {
                size = RoundUp(size, LbaSize);
                logger.Warning($"VHD size must be a multiple of {LbaSize}, rounded up size to {size}");
            }

            var maybeFooter = ReadVhdFooterSafe(path);

            return Flow<None>.Val(None.Value)
                .MapOr(_ => maybeFooter, $"Failed to read VHD footer of file {path}")
                .Check(f => size > f.CurrentSize, f => "Provided size is less or equal to the current VHD size")
                .Check(f => f.DiskType == VirtualHardDiskType.Fixed, f => "Provided disk is not a fixed VHD file")
                .Check(f => Fs.Util.HandleResizeFile(path, size, zeroFill, logger), f => "Failed to resize VHD file")
                .Map((f) =>
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
                    fs.Seek(0, SeekOrigin.End);
                    var footer = CreateVhdFooter(size);
                    fs.Write(footer.GetBytes());
                    fs.Close();

                    logger.Log("Done.");

                    return footer;
                })
                .Map(f => new VhdFileInfo(f, path, LbaSize));
        }

        public static Flow<VhdFileInfo> CreateFixedVhd(string path, ulong size, Log.ILog logger, bool forceZeroFill = false)
        {
            if (size % LbaSize > 0)
            {
                size = RoundUp(size, LbaSize);
                logger.Warning($"VHD size must be a multiple of {LbaSize}, rounded up size to {size}");
            }
            
            try
            {
                // "touch" file
                new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None).Close();
            }
            catch (Exception e)
            {
                return Flows.Err<VhdFileInfo>(e.Message);
            }
            
            if (!Fs.Util.HandleResizeFile(path, size, forceZeroFill, logger))
            {
                return Flow<VhdFileInfo>.Err("Failed to resize VHD file");
            }
            
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
            fs.Seek(0, SeekOrigin.End);
            var footer = CreateVhdFooter(size);
            fs.Write(footer.GetBytes());
            fs.Close();
                
            return Flow<VhdFileInfo>.Val(new VhdFileInfo(footer, path, LbaSize));
        }
        
        internal static ulong RoundUp(ulong numToRound, ulong multiple)
        {
            if (multiple == 0)
                return numToRound;

            ulong remainder = numToRound % multiple;
            if (remainder == 0)
                return numToRound;

            return numToRound + multiple - remainder;
        }
        
        public static VHDFooter CreateVhdFooter(ulong size)
        {
            VHDFooter vhdFooter = new VHDFooter();
            vhdFooter.OriginalSize = size;
            vhdFooter.CurrentSize = size;
            vhdFooter.SetCurrentTimeStamp();
            vhdFooter.SetDiskGeometry(size / LbaSize);

            return vhdFooter;
        }

        public static bool IsValidVhd(string path)
        {
            return OutputDiagnosticInfo(path, new NullLogger());
        }

        public static bool OutputDiagnosticInfo(string path, Log.ILog logger)
        {
            var maybeFooter = ReadVhdFooterSafe(path);

            if (maybeFooter != null)
            {
                var result = true;

                var checkString = (bool check) =>
                {
                    result = result && check;

                    return check ? "[green]valid[/]" : "[red]invalid[/]";
                };

                var diskType = maybeFooter.DiskType;
                IEnumerable<VirtualHardDiskType> dynamicTypes = [VirtualHardDiskType.Differencing, VirtualHardDiskType.Dynamic];

                var dataOffsetStr = maybeFooter.DataOffset.ToString();
                
                if (maybeFooter is { DataOffset: ulong.MaxValue, DiskType: VirtualHardDiskType.Fixed })
                {
                    dataOffsetStr = "[green]unavailable[/]";
                }
                else if (maybeFooter.DiskType == VirtualHardDiskType.Fixed)
                {
                    dataOffsetStr = $"[red]{maybeFooter.DataOffset}[/]";
                    result = false;
                }
                
                logger.Log(new Rule("[green]VHD Footer[/]").LeftJustified());
                logger.Log($"[yellow]Unique id[/]: {maybeFooter.UniqueId}");
                logger.Log($"[yellow]Disk type[/]: {diskType}");
                logger.Log($"[yellow]Current size (human readable)[/]: {maybeFooter.CurrentSize.HumanizeBytes()}");
                logger.Log($"[yellow]Current size in bytes[/]: {maybeFooter.CurrentSize}");
                logger.Log($"[yellow]Original size in bytes[/]: {maybeFooter.OriginalSize}");
                logger.Log($"[yellow]Cookie[/]: {maybeFooter.Cookie}");
                logger.Log($"[yellow]Sector size[/]: {LbaSize}");
                logger.Log($"[yellow]Timestamp[/]: {maybeFooter.TimeStamp}");
                logger.Log($"[yellow]Data offset in bytes[/]: {dataOffsetStr}");
                logger.Log($"[yellow]Footer validation[/]: {checkString(maybeFooter.IsValid)}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var isSparse = Fs.Util.IsSparseFile(path, logger);
                    string isSparseString = isSparse switch
                    {
                        true => "[red]yes[/]",
                        false => "[green]no[/]",
                        _ => "[red]error[/]"
                    };

                    logger.Log($"[yellow]Is sparse[/]: {isSparseString}");
                    result = result && !(isSparse.HasValue && isSparse.Value);
                }
                
                if (dynamicTypes.Contains(diskType))
                {
                    using var diffHandler = new DifferencingVhdHandler(path);

                    result = diffHandler.OutputDiagnosticInfo(logger) && result;
                }
                else
                {
                    var fileLength = (ulong)(new FileInfo(path).Length);
                    var validSectorCount = maybeFooter.CurrentSize + LbaSize == fileLength;
                    var checkSizeInBytesValid = maybeFooter.CurrentSize % Util.LbaSize == 0;

                    logger.Log($"[yellow]Sector count validation[/]: {checkString(validSectorCount)}");
                    logger.Log($"[yellow]Size in bytes validation[/]: {checkString(checkSizeInBytesValid)}");
                    logger.Log(new Rule("[green]End of VHD Footer[/]").LeftJustified());
                }

                return result;
            }
            else
            {
                logger.Error("Failed to read/parse VHD footer");
                return false;
            }
        }

        public static uint CalculateChecksum(byte[] data, uint checksumOffset)
        {
            uint checksum = 0;

            for (int i = 0; i < data.Length; ++i)
            {
                if (!Enumerable.Range((int)checksumOffset, 4).Contains(i))
                {
                    checksum += data[i];
                }
            }
            
            checksum = ~checksum;

            return checksum;
        }

        private static Flow<VhdFileInfo> CreateNonFixedVhd(string path, ulong? maybeSize, string? maybeParentPath, Log.ILog logger)
        {
            ulong size = 0;
            ulong parentLocatorSpaceInBytes = 0;
            var dynamicHeader = new DynamicDiskHeader();

            var parentLocatorData = new List<byte[]>();
            
            if (maybeParentPath != null)
            {
                Program.DebugOutput($"Creating child {path} for parent {maybeParentPath}");
                var parentFooter = ReadVhdFooterSafe(maybeParentPath!);

                if (parentFooter is not { IsValid: true })
                {
                    return Flow<VhdFileInfo>.Err("Failed to read/parse VHD footer");
                }
                
                size = parentFooter.CurrentSize;
                var absoluteParentPath = Path.GetFullPath(maybeParentPath);
                
                var relativeParentPath = NormalizeRelativePathToParent(Path.GetFullPath(path), Path.GetFullPath(maybeParentPath));

                if (absoluteParentPath.Length >= 256 || relativeParentPath.Length >= 256)
                {
                    return Flow<VhdFileInfo>.Err("Absolute or relative parent path is longer than 256 characters");
                }
                
                // TODO: support length > 256 symbols?
                var absolutePathBytes = Encoding.Unicode.GetBytes(absoluteParentPath.PadRight(256, '\0'));
                var relativePathBytes = Encoding.Unicode.GetBytes(relativeParentPath.PadRight(256, '\0'));
                
                parentLocatorSpaceInBytes += (ulong)absolutePathBytes.Length + (ulong)relativePathBytes.Length;
                
                parentLocatorData.Add(absolutePathBytes);
                dynamicHeader.ParentLocatorEntry1.PlatformDataLength = (uint)absoluteParentPath.Length * 2;
                dynamicHeader.ParentLocatorEntry1.PlatformDataSpace = (uint)LbaSize;
                dynamicHeader.ParentLocatorEntry1.PlatformDataOffset = LbaSize * 3;
                dynamicHeader.ParentLocatorEntry1.PlatformCode =
                    (uint)DynamicDiskHeader.ParentLocatorPlatformCode.WindowsUtf16Absolute;
                
                parentLocatorData.Add(relativePathBytes);
                dynamicHeader.ParentLocatorEntry2.PlatformDataLength = (uint)relativeParentPath.Length * 2;
                dynamicHeader.ParentLocatorEntry2.PlatformDataSpace = (uint)LbaSize;
                dynamicHeader.ParentLocatorEntry2.PlatformDataOffset = LbaSize * 4;
                dynamicHeader.ParentLocatorEntry2.PlatformCode =
                    (uint)DynamicDiskHeader.ParentLocatorPlatformCode.WindowsUtf16Relative;

                dynamicHeader.ParentUniqueID = parentFooter.UniqueId;
                dynamicHeader.ParentUnicodeName = absoluteParentPath;
            }
            else
            {
                size = maybeSize!.Value;

                if (size % LbaSize != 0)
                {
                    size = RoundUp(size, LbaSize);
                    logger.Warning($"VHD size must be a multiple of {LbaSize}, rounded up size to {size}");
                }
            }
            
            var footer = CreateVhdFooter(size);
            footer.DiskType = maybeParentPath != null ? VirtualHardDiskType.Differencing : VirtualHardDiskType.Dynamic;
            footer.DataOffset = LbaSize;
            
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);

            dynamicHeader.TableOffset = LbaSize * 3 + parentLocatorSpaceInBytes;
            dynamicHeader.BlockSize = 1024 * 1024 * 2; // 2MiB
            dynamicHeader.MaxTableEntries = (uint)size.DivideAndCeil(dynamicHeader.BlockSize);
            
            ulong batSize = sizeof(uint) * dynamicHeader.MaxTableEntries;
            ulong batSpace = RoundUp(batSize, LbaSize);

            var footerBytes = footer.GetBytes();
            var dynamicHeaderBytes = dynamicHeader.GetBytes();
            
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(footerBytes);
            stream.Write(dynamicHeaderBytes);
            
            byte[] gapBeforeBatBytes = new byte[dynamicHeader.TableOffset - (ulong)footerBytes.Length - (ulong)dynamicHeaderBytes.Length];
            stream.Write(gapBeforeBatBytes);

            if (parentLocatorData.Any())
            {
                var locatorEntries = dynamicHeader.GetParentLocatorEntries();

                foreach (var (locatorEntry, idx) in locatorEntries.Select((locatorEntry, idx) => (locatorEntry, idx)))
                {
                    stream.Seek((long)locatorEntry.PlatformDataOffset, SeekOrigin.Begin);
                    stream.Write(parentLocatorData[idx]);
                }
            }
            
            byte[] bat = Enumerable.Repeat((byte)0xFF, (int)batSpace).ToArray();
            
            stream.Seek((long)dynamicHeader.TableOffset, SeekOrigin.Begin);
            stream.Write(bat);
            stream.Write(footerBytes);
            stream.Close();

            return Flow<VhdFileInfo>.Val(new VhdFileInfo(footer, path, LbaSize));
        }
        
        // Beware, passing strange things in you will get strange things out
        // See tests for covered scenarios. I hate this, but I don't have better solution today
        // This function tries to mimic what Windows does when creating differencing VHDs
        // It also returns relative paths with \ as directory separator on all platforms to avoid confusing Windows
        // For supported scenarios this function produces identical results on Windows and Posix systems (hopefully)
        internal static string NormalizeRelativePathToParent(string c, string p)
        {
            Regex isRootedWinPath = new Regex(@"^[\W\w]\:\\.*");
            Regex isRootedPosixPath = new Regex(@"^\/");

            if (!isRootedPosixPath.IsMatch(p) && !isRootedWinPath.IsMatch(p))
            {
                throw new InvalidPathException(p);
            }
            else if (!isRootedPosixPath.IsMatch(c) && !isRootedWinPath.IsMatch(c))
            {
                throw new InvalidPathException(c);
            }

            if (isRootedWinPath.IsMatch(p) != isRootedWinPath.IsMatch(c))
            {
                throw new InvalidPathException(c);
            }

            if (isRootedWinPath.IsMatch(p) && isRootedWinPath.IsMatch(c) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (c[0].ToString().ToUpper() != p[0].ToString().ToUpper())
                {
                    return p;
                }
                
                var posixify = (string s) => "/" + s[0] + s.Substring(2).Replace("\\", "/");

                c = posixify(c);
                p = posixify(p);
            }
            
            var pDir = Path.GetDirectoryName(p);
            var cDir = Path.GetDirectoryName(c);

            if (cDir != null && pDir != null && cDir == pDir)
            {
                return @".\" + Path.GetFileName(p).Replace('/', '\\');
            }
            else if (cDir != null)
            {
                var result = Path.GetRelativePath(cDir, p);

                if (!isRootedWinPath.IsMatch(result) && !result.StartsWith('.'))
                {
                    return @".\" + result.Replace('/', '\\');
                }
                else
                {
                    return result.Replace('/', '\\');
                }
            }
            else
            {
                return @".\" + Path.GetFileName(p).Replace('/', '\\');
            }
        }
        
        // This is needed because differencing disk parent locator logic is borked upstream
        public static Flow<DiscUtils.Vhd.Disk> OpenDiskWithDu(string path, Log.ILog logger)
        {
            var maybeFooter = Util.ReadVhdFooterSafe(path);

            if (maybeFooter == null)
            {
                return Flows.Err<DiscUtils.Vhd.Disk>("Failed to read VHD footer");
            }

            var footer = maybeFooter;

            if (footer.DiskType != VirtualHardDiskType.Differencing)
            {
                return Flows.Val(new DiscUtils.Vhd.Disk(path, FileAccess.Read));
            }
            
            var diskType = footer.DiskType;
            var diskPath = path;

            List<DiscUtils.Vhd.DiskImageFile> layers = [new DiscUtils.Vhd.DiskImageFile(diskPath, FileAccess.Read)];
            
            while (diskType == VirtualHardDiskType.Differencing)
            {
                using var diffHandler = new DifferencingVhdHandler(diskPath);
                
                var parentLocations = diffHandler.FindParentLocation().ToList();
                
                var found = parentLocations.Any();

                if (found)
                {
                    var layer = new DiskImageFile(parentLocations.First(), FileAccess.Read);
                    layers.Add(layer);
                    diskType = ConvertVhdTypeFromDuToDal(layer.Information.DiskType);
                    diskPath = parentLocations.First();
                }
                else
                {
                    foreach (var locator in diffHandler.ReadParentLocators())
                    {
                        logger.Error($"Locator entry: ({(DynamicDiskHeader.ParentLocatorPlatformCode)locator.Item1.PlatformCode}) ({locator.Item2})");
                    }

                    return Flows.Err<DiscUtils.Vhd.Disk>("Failed to find parent VHD");
                }
            }

            return Flows.Val(new DiscUtils.Vhd.Disk(layers, Ownership.Dispose));
        }

        public static FileType ConvertVhdTypeFromDalToDu(VirtualHardDiskType type)
        {
            return type switch
            {
                VirtualHardDiskType.Differencing => FileType.Differencing,
                VirtualHardDiskType.Dynamic => FileType.Dynamic,
                VirtualHardDiskType.Fixed => FileType.Fixed,
                _ => FileType.None
            };
        }

        public static VirtualHardDiskType ConvertVhdTypeFromDuToDal(FileType type)
        {
            return type switch
            {
                FileType.Differencing => VirtualHardDiskType.Differencing,
                FileType.Dynamic => VirtualHardDiskType.Dynamic,
                _ => VirtualHardDiskType.Fixed
            };
        }
    }
}