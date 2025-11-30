#if WINDOWS
using System.Runtime.Versioning;
using auvdisk.Cli;
using auvdisk.DiskImage;
using auvdisk.Log;
using auvdisk.Extensions;
using DiscUtils;

namespace auvdisk.Interop.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public class BcdBootloaderInstaller(ILog initLogger)
{
    internal record BootableWindowsLayout(
        string EfiVolumePath, 
        char EfiTargetLetter, 
        char? SourceWindowsLetter);
    
    private ILog Logger => initLogger;
    private char EfiTargetLetter => 'X'; // TODO: ideally, check that it is not already taken 
    
    public Flow<None> InstallBootloader(string target)
    {
        var logger = Logger;
        var opts = new { target, logger };
            
        char? targetLetter = target switch 
        {
            [var l] => l, // C
            [var l, _] => l, // C:
            [var l, _, _] => l, // C:\
            _ => null
        };
        
        var isMountedDisk = targetLetter.HasValue;
        var isVhd = !isMountedDisk && new DiskProbe(target, logger).Probe().MapTo(x => x.IsSuccess() && new[]{"VHD", "VHDX"}.Contains(x.Disk?.ImageType ?? ""));
        
        if (isMountedDisk)
        {
            return FindBootableWindowsLayoutInMounted(targetLetter!.Value)
                .Bind(v => InitializeEfiBootPartitionWithWinBcdBootloader(
                    v.EfiVolumePath, v.EfiTargetLetter, v.SourceWindowsLetter!.Value))
                .Map(_ => None.Value);
        }
        else if (isVhd)
        {
            bool shouldUnmount = false;
            
            var result = VhdMounter.Mount(opts.target, opts.logger)
                .SideEffect(_ => shouldUnmount = true)
                .Bind(FindBootableWindowsLayoutInVhd)
                .SideEffectIf( // TODO: add confirm if fallback is effective
                    (layout, _) => !layout.SourceWindowsLetter.IsSome(), // ugly fallback to C, but I don't have better solution at the moment
                    (_, _) => logger.Warning("Failed to find source data volume, falling back to C"))
                .Map(v => v with { SourceWindowsLetter = v.SourceWindowsLetter ?? 'C' })
                .Bind(
                    v => InitializeEfiBootPartitionWithWinBcdBootloader(
                        v.EfiVolumePath, v.EfiTargetLetter, v.SourceWindowsLetter!.Value))
                .Map(_ => None.Value);

            var unmountResult = Flows.Val(None.Value)
                .BindErrIf(
                    _ => shouldUnmount,
                    _ => VhdMounter.Dismount(opts.target, opts.logger).Flow($"Failed to dismount {opts.target}"));
            
            return result.BindErr(_ => unmountResult);
        }
        else
        {
            return new($"Unknown target {target}");
        }
    }
    
    private Flow<BootableWindowsLayout> FindBootableWindowsLayoutInVhd(IEnumerable<VhdMounter.VhdVolumeInfo> volumes)
    {
        var vhdVolumeInfos = volumes.ToList();
        var efiVolume = vhdVolumeInfos.FirstOrDefault(v => v.FileSystem == "FAT32");
        var maybeDataVolume = vhdVolumeInfos.FirstOrDefault(v => v.FileSystem == "NTFS");

        return Flows.ValOr(efiVolume, "Failed to detect/find EFI/data volumes")
            .Map(efi => new BootableWindowsLayout(
                EfiVolumePath: efi.Path, 
                EfiTargetLetter: EfiTargetLetter, 
                SourceWindowsLetter: maybeDataVolume?.DriveLetter));
    }

    internal Flow<BootableWindowsLayout> FindBootableWindowsLayoutInMounted(char mountedVolumeLetter)
    {
        var logger = Logger;
        
        return Common.GetVolumes(logger)
            .Map(volumes => FindVolumesOnSameDevice(volumes.ToList(), mountedVolumeLetter))
            .MapOr(volumes => volumes.Where(v =>
                {
                    using var fs = Common.OpenPartitionByIdReadonly(v.DeviceId, logger);
                    var fsInfoList = FileSystemManager.DetectFileSystems(fs);
                    
                    return fsInfoList.Any(x => x.Name == "FAT");
                }).FirstOrDefault(), "Failed to find FAT32 volume")
            .MapOr(v => v.MountPoints.FirstOrDefault(x => x.Contains("Volume")), "Failed to find FAT32 volume mount point")
            .Map(v => new BootableWindowsLayout(v, EfiTargetLetter, mountedVolumeLetter));
    }
    
    private Flow<Value<char>> InitializeEfiBootPartitionWithWinBcdBootloader(string efiVolumePath, char efiTargetLetter, char sourceWindowsLetter)
    {
        var logger = Logger;
        var rq =  new { efiVolumePath, efiTargetLetter, sourceWindowsLetter, logger };
            
        return Flows.Val(rq)
            .Check(
                opts => !ContainsEfiBootloader(opts.efiVolumePath), 
                opts => $"Target volume <{opts.efiVolumePath}> already contains EFI bootloader")
            .Check(
                opts => ContainsWindows(opts.sourceWindowsLetter),
                opts => $"Source volume <{opts.sourceWindowsLetter}> does not contain Windows")
            .BindErr(opts => DriveLetterManager.AddDriveLetterToVolume(opts.efiVolumePath, opts.efiTargetLetter, opts.logger))
            .BindErr(opts => CliTools.ExecuteBcdBoot(opts.efiTargetLetter, opts.sourceWindowsLetter, opts.logger))
            .Bind(opts => DriveLetterManager.RemoveDriveLetterFromVolume(opts.efiTargetLetter, opts.logger));
    }
    
    private static IEnumerable<PhysicalVolumeInfo> FindVolumesOnSameDevice(List<PhysicalVolumeInfo> volumes, char letter)
    {
        var maybeVolume = volumes.FirstOrDefault(v => v.MountPoints.Contains(@$"{letter}:\"));

        return maybeVolume.IsSome() ? volumes.Where(x => x.ParentDeviceId == maybeVolume!.ParentDeviceId) : [];
    }

    private static bool ContainsEfiBootloader(string volumePath)
    {
        var target = $"{volumePath}EFI";
            
        return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
    }

    private static bool ContainsWindows(char volumeLetter)
    {
        var target = @$"{volumeLetter}:\Windows";
            
        return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
    }
}
#endif