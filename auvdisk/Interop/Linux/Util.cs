using auvdisk.Log;

namespace auvdisk.Interop.Linux
{
    public static class Util
    {
        public static Stream OpenPartitionByName(string name, ILog logger)
        {
            return new BlockDeviceStream(name);
        }
    }
}