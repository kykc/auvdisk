#if WINDOWS
using System.Management.Automation;
using System.Collections.ObjectModel;
using System.Management.Automation.Runspaces;
using auvdisk.Extensions;
using auvdisk.Log;
using Microsoft.PowerShell;

namespace auvdisk.Interop.Win32
{
    public static class VhdMounter
    {
        public record VhdVolumeInfo(string Path, string UniqueId, ulong SizeInBytes, string FileSystem, char? DriveLetter);
        
        public static Flow<IEnumerable<VhdVolumeInfo>> Mount(string vhdPath, ILog logger)
        {
            if (!File.Exists(vhdPath))
            {
                return Flow<IEnumerable<VhdVolumeInfo>>.Err($"VHD file {vhdPath} not found");
            }
            
            // -PassThru returns the disk object
            // We pipe it to Get-Partition and Get-Volume to find the drive letter
            var script = $@"
                Mount-Vhd -Path '{vhdPath}' -PassThru | 
                Get-Partition | 
                Get-Volume | 
                Select-Object
            ";
            
            var state = InitialSessionState.CreateDefault();
            state.ExecutionPolicy = ExecutionPolicy.Bypass;

            using var ps = PowerShell.Create(state);
            ps.AddScript(script);

            try
            {
                logger.Log($"Mounting {vhdPath}...");
                Collection<PSObject> results = ps.Invoke();
                    
                if (ps.Streams.Error.Count > 0)
                {
                    foreach (var error in ps.Streams.Error)
                    {
                        logger.Error($"[PS Error] {error}");
                    }

                    return Flow<IEnumerable<VhdVolumeInfo>>.Err("PS Errors found");
                }
                    
                var infos = results.Select(x => new VhdVolumeInfo(
                    x.Properties["Path"].Value.ToString()!, 
                    x.Properties["UniqueId"].Value.ToString()!, 
                    (ulong)x.Properties["Size"].Value, 
                    x.Properties["FileSystem"].Value.ToString()!,
                    x.Properties["DriveLetter"].Value?.ToString()?.First())).ToList();


                if (!infos.Any())
                {
                    logger.Warning("VHD mounted, but no volumes was returned.");
                }

                logger.Log($"Successfully mounted {vhdPath}.");
                return Flow<IEnumerable<VhdVolumeInfo>>.Val(infos);
            }
            catch (Exception ex) when (Program.ExceptionFilter(ex))
            {
                return Flow<IEnumerable<VhdVolumeInfo>>.Err(ex.Message);
            }
        }
        
        public static bool Dismount(string vhdPath, ILog logger)
        {
            string script = $"Dismount-Vhd -Path '{vhdPath}'";
            logger.Log($"Dismounting {vhdPath}...");
            InitialSessionState state = InitialSessionState.CreateDefault();
            state.ExecutionPolicy = ExecutionPolicy.Bypass;

            using var ps = PowerShell.Create(state);
            ps.AddScript(script);
            
            try
            {
                ps.Invoke();
                if (ps.Streams.Error.Count > 0)
                {
                    foreach (var error in ps.Streams.Error)
                    {
                        logger.Error($"[PS Error] {error}");
                    }

                    return false;
                }
                else
                {
                    logger.Log($"Successfully dismounted {vhdPath}.");

                    return true;
                }
            }
            catch (Exception ex) when (Program.ExceptionFilter(ex))
            {
                logger.Error($"Dismount failed: {ex.Message}");
                return false;
            }
        }
    }    
}
#endif