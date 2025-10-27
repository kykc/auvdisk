using DiscUtils.Streams;

namespace auvdisk.test.Qcow2
{
    [Collection("Sequential")]
    public class Qcow2StreamTest
    {
        [Fact]
        public void TestAsRawDuDisk()
        {
            var logger = new LogWatcher();

            string qcow2Path = Path.Join("testdata", "test_gpt.qcow2");
            string referencePath = Path.Join("testdata", "test_gpt.vhd");

            using var fs = File.OpenRead(qcow2Path);
            var qcow2 = new Bytes.Qcow2Stream(fs);

            using var reference = DiscUtils.VirtualDisk.OpenDisk(referencePath, FileAccess.Read);
            var qcow2Disk = new DiscUtils.Raw.Disk(qcow2, Ownership.None);

            reference.Content.Seek(0, SeekOrigin.Begin);
            qcow2Disk.Content.Seek(0, SeekOrigin.Begin);

            var referenceHash = TestUtil.CalcSha256Hash(reference.Content);
            var qcow2Hash = TestUtil.CalcSha256Hash(qcow2Disk.Content);

            Assert.Equal(reference.Content.Length, qcow2Disk.Content.Length);
            Assert.Equal(referenceHash, qcow2Hash);
        }
    }
}