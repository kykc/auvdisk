using System.Security.Cryptography;
using auvdisk.DiskImage.Vhd;
using auvdisk.Extensions;
using auvdisk.Log;
using DiscUtils;
using DiscUtils.Streams;
using DiskAccessLibrary.VHD;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace auvdisk.test;

public static class TestUtil
{
    public static VHDFooter? ReadVhdFooter(string path)
    {
        if (File.Exists(path))
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);

            if (stream.Length > 512)
            {
                stream.Seek(-512, SeekOrigin.End);
                var footerBytes = new byte[512];
                stream.ReadExactly(footerBytes);
                
                return new VHDFooter(footerBytes);
            }
        }

        return null;
    }
    
    public static string CalcSha256Hash(string file)
    {
        using FileStream stream = File.OpenRead(file);

        return CalcSha256Hash(stream);
    }

    public static string CalcSha256Hash(Stream stream)
    {
        var sha = SHA256.Create();
        byte[] checksum = sha.ComputeHash(stream);
        return BitConverter.ToString(checksum).Replace("-", String.Empty);
    }
    
    public static uint LazyFastDiskHash(DiscUtils.VirtualDisk disk)
    {
        var streams = disk.Content.Extents.Select(x => new SubStream(disk.Content, Ownership.None, x.Start, x.Length));
        return CalculateAdler32(streams);
    }
    
    internal static uint CalculateAdler32(IEnumerable<Stream> streams)
    {
        const uint modAdler = 65521;
        
        uint a = 1;
        uint b = 0;
        
        const int bufferSize = 1024 * 1024; // 1MiB 
        var buffer = new byte[bufferSize];

        foreach (var stream in streams)
        {
            int bytesRead;
            stream.Seek(0, SeekOrigin.Begin);

            while ((bytesRead = stream.Read(buffer, 0, bufferSize)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    // A is the sum of the data bytes (modulo MOD_ADLER)
                    a = (a + buffer[i]) % modAdler;

                    // B is the sum of the previous values of A (modulo MOD_ADLER)
                    b = (b + a) % modAdler;
                }
            }
        }

        // The final checksum is (B * 65536) + A
        return (b << 16) | a;
    }

    // This one "cleanly" clones the VHD file chain to the new location, all target disks get new UIDs
    // and their parent locators point to the correct files (both absolute and relative)
    // If you think about it, just copying VHD file chain as files can lead to very ambiguous situation:
    // * Imagine you have parent/child pair (C:\parent.vhd, C:\child.vhd)
    // * Now you copy those files to C:\test, so you now have another pair (C:\test\parent.vhd, C:\test\child.vhd)
    // * Now, you try to open/mount C:\test\child.vhd
    // * Which parent it will try to relate to? C:\parent.vhd or C:\test\parent.vhd?
    // * Child VHD itself will have two parent locators: with relative and absolute paths to the parent respectfully
    // * In this case they will be (.\parent.vhd and C:\parent.vhd)
    // * And now for C:\test\child.vhd one of them points to one file, while other points to the other file
    // * And both files are perfectly available. So, we got a potential UB.
    // NOTE: In such case Windows prefers relative path and updates absolute path to the new one in the child VHD on mount
    public static Flow<List<string>> CloneVhdChain(string sourcePath, string targetDir, string prefix, ILog logger)
    {
        // I transform layers extracting only the metadata I need.
        // I dispose of the disk itself early, so it seems correct not to pass objects that the disk owns further
        var diskLayerToData = (VirtualDiskLayer layer) 
            => new { layer.IsSparse, layer.FullPath, layer.Capacity};
        
        return Flows.Val(None.Value)
            .Check(_ => Directory.Exists(targetDir), _ => "Target directory does not exist")
            .Check(_ => File.Exists(sourcePath), _ => "Source VHD does not exist")
            .TryBind(_ => Util.OpenDiskWithDu(sourcePath, logger), (Exception e) => $"Failed to open source image with error: {e.Message}")
            .MapDispose(disk =>
            {
                var layers = disk.Layers.ToList();
                var solidLayer = layers.AsEnumerable().Reverse().Select(diskLayerToData).First();
                var layersTail = layers.AsEnumerable().Reverse().Skip(1).Select(diskLayerToData).ToList();
                var parentTarget = Path.Join(targetDir, $"{prefix}_{Path.GetFileName(solidLayer.FullPath)}");

                return new { solidLayer, layersTail, parentTarget };
            })
            .BindConcat(
                x => DiskImage.Util.CreateVdisk(x.parentTarget, (ulong)x.solidLayer.Capacity, logger, x.solidLayer.IsSparse, false, false),
                (x, y) => new { x.solidLayer, x.layersTail, x.parentTarget, targetDisk = y })
            .BindConcat(x => // BindConcat instead of CheckDiscard to retain original result, omitting now disposed x.targetDisk
            {
                using var targetDisk = x.targetDisk;
                using var solidLayerDisk = VirtualDisk.OpenDisk(x.solidLayer.FullPath, FileAccess.Read);

                return DiskImage.Util.LazyCopyDiskContents(solidLayerDisk, targetDisk, logger);
            }, (x, y) => new { x.solidLayer, x.layersTail, x.parentTarget })
            .Bind(x =>
            {
                var parentTarget = x.parentTarget;
                var createdFiles = new List<string> { parentTarget };
                
                foreach (var layer in x.layersTail)
                {
                    var layerTarget = Path.Join(targetDir, $"{prefix}_{Path.GetFileName(layer.FullPath)}");
                    
                    var layerCreateResult = Util.CreateDifferentialVhd(parentTarget, layerTarget, logger);

                    if (layerCreateResult.IsErr)
                    {
                        return new(layerCreateResult.UnwrapErr());
                    }
                    
                    using var layerDisk = VirtualDisk.OpenDisk(layerTarget, FileAccess.ReadWrite);

                    var diffHandler = new DifferencingVhdHandler(layer.FullPath);
                    diffHandler.MergeChangedSectorsIntoParent(layerDisk.Content, logger);

                    parentTarget = layerTarget;
                    createdFiles.Add(layerTarget);
                }

                return Flows.Val(createdFiles);
            });
    }
}
