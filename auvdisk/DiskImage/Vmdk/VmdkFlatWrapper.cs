namespace auvdisk.DiskImage.Vmdk
{
    internal static class Ext
    {
        // Reference file had Windows newlines, so I use them here explicitly
        internal static string Line(this string source, string line)
        {
            source += line + "\r\n";

            return source;
        }

        // Beware, no escaping, this is just for the sakes of this template
        internal static string Quote(this string source)
        {
            return "\"" + source + "\"";
        }
    }

    // See auvdisk.test/testdata/reference.vmdk to see what this thing tries to mimic/recreate
    public class VmdkFlatWrapper
    {
        public override string ToString()
        {
            return ""
                .Line($"# Disk DescriptorFile")
                .Line($"version={Version}")
                .Line($"encoding={Encoding.Quote()}")
                .Line($"CID={Cid}")
                .Line($"parentCID={ParentCid}")
                .Line($"createType={CreateType.Quote()}")
                .Line($"")
                .Line($"# Extent description")
                .Line($"RW {LengthInSectors} FLAT {Filename.Quote()} 0")
                .Line($"")
                .Line($"# The Disk Data Base")
                .Line($"#DDB")
                .Line($"")
                .Line($"ddb.adapterType = {AdapterType.Quote()}")
                .Line($"ddb.geometry.cylinders = {DdbCylinders.ToString().Quote()}")
                .Line($"ddb.geometry.heads = {DdbHeads.ToString().Quote()}")
                .Line($"ddb.geometry.sectors = {DdbSectors.ToString().Quote()}")
                .Line($"ddb.longContentID = {DdbLongContentId.ToString().Replace("-", "").Quote()}")
                .Line($"ddb.toolsInstallType = {DdbToolsInstallType.Quote()}")
                .Line($"ddb.toolsVersion = {DdbToolsVersion.Quote()}").TrimEnd('\n', '\r');
        }

        private VmdkFlatWrapper()
        {
        }

        public static VmdkFlatWrapper? Create(string rawImagePath, Log.ILog logger)
        {
            var lengthInBytes = new FileInfo(rawImagePath).Length;
            var filename = Path.GetFileName(rawImagePath);

            var result = new VmdkFlatWrapper
            {
                LengthInBytes = (ulong)lengthInBytes,
                Filename = filename,
            };

            return PerformSanityCheck(result, logger) ? result : null;
        }

        public static VmdkFlatWrapper? Create(string filename, string cid, Guid longContentId, ulong lengthInBytes, Log.ILog logger)
        {
            var result = new VmdkFlatWrapper
            {
                LengthInBytes = lengthInBytes,
                Filename = filename,
                Cid = cid,
                DdbLongContentId = longContentId
            };

            return PerformSanityCheck(result, logger) ? result : null;
        }

        private static bool PerformSanityCheck(VmdkFlatWrapper subject, Log.ILog logger)
        {
            if (subject.LengthInBytes % subject.LbaSize != 0)
            {
                logger.Error($"Subject length in bytes {subject.LengthInBytes} must be multiple of LBA size of {subject.LbaSize}");

                return false;
            }

            return true;
        }

        public uint Version => 1;
        public string Encoding => "UTF-8";
        public string ParentCid => "ffffffff";
        public string Cid { get; set; } = string.Join("", Enumerable.Range(0, 4).Select(_ => (byte)new Random().Next(256)).Select(b => b.ToString("X2"))).ToLower();
        public string CreateType => "monolithicFlat";
        public string AdapterType { get; set; } = "lsilogic"; // Another option that I know of is ide
        public string DdbToolsInstallType => "4"; // It's enquoted in file format, so let it be string
        public string DdbToolsVersion => "12325"; // Same here
        public string Filename { get; set; } = "";
        public Guid DdbLongContentId { get; set; } = Guid.NewGuid();
        public ulong DdbHeads { get; set; } = 255;
        public ulong DdbSectors { get; set; } = 63;
        public ulong LbaSize => 512;
        public ulong LengthInBytes { get; set; } = 0;
        public ulong LengthInSectors => LengthInBytes / LbaSize;
        public ulong DdbCylinders => LengthInSectors / (DdbHeads * DdbSectors); // It seems to be okay if there is a remainder involved here

    }
}
