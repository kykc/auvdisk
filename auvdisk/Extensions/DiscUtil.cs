namespace auvdisk.Extensions
{
    public static class DiscUtil
    {
        public static string FormatDuPath(this string path, bool pretty = true)
        {
            if (pretty)
            {
                return path.TrimStart(new char[] { '\\', '/' }).Replace("\\", "/");
            }
            else
            {
                return path.TrimStart(new char[] { '\\', '/' }).Replace("/", "\\");
            }
        }
    }
}