using auvdisk.Extensions;

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
}