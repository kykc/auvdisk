using auvdisk.Extensions;
using DiscUtils.Streams;
using DiscUtils.Vhdx;

namespace auvdisk.DiskImage.Vhdx;

public static class Util
{
    public static Flow<Disk> CreateFixed(string path, ulong size, Log.ILog logger, bool forceZeroFill = false)
    {
        var rq = new { path, size, forceZeroFill, logger };

        return Flows.Val(rq)
            .HandleAll()
            // "touch" file
            .SideEffect(opts => new FileStream(opts.path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None).Close())
            .SideEffectIf(
                opts => !Fs.Util.HandleResizeFile(opts.path, opts.size, opts.forceZeroFill, opts.logger),
                opts => opts.logger.Warning("Failed to resize file, falling back to DiscUtils. Full zero-fill w/o progress report might happen"))
            .MapConcat(
                opts => new FileStream(opts.path, FileMode.Open, FileAccess.ReadWrite),
                (opts, stream) => new { opts, stream })
            .Bind((state, ctx) =>
            {
                var disk = Disk.InitializeFixed(state.stream, Ownership.Dispose, (long)state.opts.size);
                ctx.RemoveDisposable(state.stream); // Now managed by the disk instance
                return Flows.ValOr(disk, "Failed to create fixed VHDx image");
            })
            .PopHandler();
    }

    public static Flow<Disk> CreateDynamic(string path, ulong size, Log.ILog logger)
    {
        var rq = new { path, size, logger };

        return Flows.Val(rq)
            .HandleAll()
            .MapConcat(
                opts => new FileStream(opts.path, FileMode.CreateNew, FileAccess.ReadWrite),
                (opts, stream) => new { opts, stream })
            .Bind((state, ctx) =>
            {
                var disk = Disk.InitializeDynamic(state.stream, Ownership.Dispose, (long)size);
                ctx.RemoveDisposable(state.stream); // Now managed/consumed by the disk instance
                
                return Flows.ValOr(disk, "Failed to create dynamic VHDx image");
            })
            .PopHandler();
    }

    // TODO: investigate VHDx parent locators. Need to check what DU writes here and is it sensible enough
    public static Flow<Disk> CreateDifferencing(string path, string parentPath, Log.ILog logger)
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
            .PopHandler();
    }

    public static IDictionary<string, string> GetParentLocators(string path)
    {
        var image = new DiskImageFile(path, FileAccess.Read);

        var info = image.Information;

        return info.ParentLocatorEntries;
    }
}