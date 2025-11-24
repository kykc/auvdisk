using auvdisk.Extensions;
using DiscUtils.Streams;
using DiscUtils.Vhdx;

namespace auvdisk.DiskImage.Vhdx;

public static class Util
{
    public static Flow<DiscUtils.Vhdx.Disk> CreateFixed(string path, ulong size, Log.ILog logger, bool forceZeroFill = false)
    {
        try
        {
            // "touch" file
            new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None).Close();
        }
        catch (Exception e) when (Program.ExceptionFilter(e))
        {
            return Flows.Err<DiscUtils.Vhdx.Disk>(e.Message);
        }
            
        if (!Fs.Util.HandleResizeFile(path, size, forceZeroFill, logger))
        {
            logger.Warning("Failed to resize file, falling back to DiscUtils. Full zero-fill w/o progress report might happen");
        }
        
        var targetStream =
            new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        
        var disk = DiscUtils.Vhdx.Disk.InitializeFixed(targetStream, DiscUtils.Streams.Ownership.Dispose, (long)size)!;

        // Do not trust third-party libraries and their null-annotations
        return Flows.ValOr(disk, "Failed to create fixed VHDx image");
    }

    public static Flow<DiscUtils.Vhdx.Disk> CreateDynamic(string path, ulong size, Log.ILog logger)
    {
        var targetStream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);

        var disk = DiscUtils.Vhdx.Disk.InitializeDynamic(targetStream, DiscUtils.Streams.Ownership.Dispose, (long)size)!;
        
        // Do not trust third-party libraries and their null-annotations
        return Flows.ValOr(disk, "Failed to create dynamic VHDx image");
    }

    // TODO: investigate VHDx parent locators. Need to check what DU writes here and is it sensible enough
    public static Flow<DiscUtils.Vhdx.Disk> CreateDifferencing(string path, string parentPath, Log.ILog logger)
    {
        return Flows.Val(None.Value)
            .HandleAll()
            .Map(_ => new DiskImageFile(parentPath, FileAccess.ReadWrite))
            .MapConcat(
                _ => new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite),
                (parentDisk, subjectStream) => new { parentDisk, subjectStream })
            .MapOr(state =>
            {
                string parentAbsolutePath = Path.GetFullPath(parentPath);
                string parentRelativePath = Vhd.Util.NormalizeRelativePathToParent(Path.GetFullPath(path), Path.GetFullPath(parentPath));

                return Disk.InitializeDifferencing(state.subjectStream, Ownership.Dispose, state.parentDisk, Ownership.Dispose,
                    parentAbsolutePath, parentRelativePath, DateTime.UtcNow);
            }, "Failed to create differencing VHDx image file")
            .PopCtx();
    }
}