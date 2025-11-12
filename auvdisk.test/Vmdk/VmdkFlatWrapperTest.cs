namespace auvdisk.test.Vmdk
{
    public class VmdkFlatWrapperTest
    {
        public VmdkFlatWrapperTest()
        {
            Program.IsInteractive = false;
        }
        
        [Fact]
        public void TestAgainstReference()
        {
            var logger = new LogWatcher();

            string referencePath = Path.Join("testdata", "reference.vmdk");
            using var referenceStream = new FileStream(referencePath, FileMode.Open, FileAccess.Read);
            var streamReader = new StreamReader(referenceStream);
            var reference = streamReader.ReadToEnd();

            var vmdk = DiskImage.Vmdk.VmdkFlatWrapper.Create("ubuntu2204.img", "4363dd6b",
                Guid.Parse("51a1f41312ec3ab670076aca4363dd6b"), 107374182400, logger);

            Assert.NotNull(vmdk);
            Assert.Equal(reference, vmdk.ToString());
        }
    }
}