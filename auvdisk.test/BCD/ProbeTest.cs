using auvdisk.BCD;

namespace auvdisk.test.BCD
{
    public class ProbeTest
    {
        private static string Target => Path.Join(Directory.GetCurrentDirectory(), "testdata", "BCD");

        [Fact]
        public void TestBcdProbe()
        {
            var logger = new LogWatcher();

            var records = Util.ProbeBcd(Target, false, logger).ToList();

            Assert.Equal(2, records.Count);

            var win11Native =  records.First() as WindowsOsLoaderBcdRecord;
            var win10Vhd = records.Skip(1).First() as WindowsOsLoaderBcdRecord;

            Assert.NotNull(win11Native);
            Assert.NotNull(win10Vhd);
            Assert.Equal("Windows 10 VHD", win10Vhd.HumanReadableName);
            Assert.Equal("Windows 11 Partition", win11Native.HumanReadableName);

            const string win10Device =
                @"(disk:f2b059dc-c895-4fb4-ba0c-3d0a25ae7f43 partition:f696c74c-14bb-480d-b418-b8e632fc7528)\win10.vhd";

            const string win11Device = @"(disk:bcdec46d-f06a-4cd5-b45f-31df5fed4431 partition:c9dcbb1d-76af-4737-981b-a91654a6b360)";

            Assert.Equal(win10Device, win10Vhd.Device);
            Assert.Equal(win10Device, win10Vhd.OsDevice);

            Assert.Equal(win11Device, win11Native.Device);
            Assert.Equal(win11Device, win11Native.OsDevice);
        }
    }
}