#if WINDOWS
using System.ComponentModel;
using System.Management;
using System.Runtime.Versioning;
using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.Interop.Win32
{
    [SupportedOSPlatform("windows5.1.2600")]
    public static class DriveLetterManager
    {
        public static Flow<Value<char>> AddDriveLetterToVolume(string volumeGuidPath, char driveLetter, ILog logger)
        {
            var wmiPath = volumeGuidPath;
            var query = $"SELECT * FROM Win32_Volume WHERE DeviceID = '{wmiPath.Replace(@"\", @"\\")}'";

            try
            {
                var searcher = new ManagementObjectSearcher("root\\CIMV2", query);
                ManagementObject? volume = searcher.Get()?.Cast<ManagementObject>()?.FirstOrDefault();

                if (volume == null)
                {
                    return Flow<Value<char>>.Err($"Volume not found: {wmiPath}");
                }
                
                var mountPoint = $"{driveLetter}:\\";
                
                var methodParams = volume.GetMethodParameters("AddMountPoint");
                methodParams["Directory"] = mountPoint;
                
                var result = volume.InvokeMethod("AddMountPoint", methodParams, null);

                var returnValue = (uint)result["ReturnValue"];
                
                if (returnValue == 0)
                {
                    logger.Log($"Successfully mounted {volumeGuidPath} to {mountPoint}");
                    return Flow<Value<char>>.Val(new Value<char>(driveLetter));
                }
                else
                {
                    // Source: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/vdswmi/addmountpoint-method-in-class-win32-volume
                    string error = returnValue switch
                    {
                        0 => "Success",
                        1 => "Access Denied",
                        2 => "Invalid Argument",
                        3 => "Specified Directory Not Empty",
                        4 => "Specified Directory Not Found",
                        5 => "Volume Mount Points Not Supported",
                        6 => "Unknown Error <6>",
                        _ => $"Unknown Error <{returnValue}>"
                    };
                    
                    return Flow<Value<char>>.Err($"Failed to mount {volumeGuidPath} to {mountPoint} with error [{error}]");
                }
            }
            catch (Exception ex)
            {
                return Flow<Value<char>>.Err(ex.Message);
            }
        }
        
        public static Flow<Value<char>> RemoveDriveLetterFromVolume(char driveLetter, ILog logger)
        {
            if (!Windows.Win32.PInvoke.DeleteVolumeMountPoint($"{driveLetter}:\\"))
            {
                return Flow<Value<char>>.Err(new Win32Exception().Message);
            }
            
            logger.Log($"Successfully dismounted <{driveLetter}>");
            return Flow<Value<char>>.Val(new Value<char>(driveLetter));
        }
    }
}
#endif