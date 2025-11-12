namespace auvdisk.test
{
    [CollectionDefinition("Sequential")]
    public class SequentialCollection : ICollectionFixture<TestFixture>
    {
    }

    public class TestFixture : IDisposable
    {
        public TestFixture()
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();
            Program.IsInteractive = false;
        }

        public void Dispose()
        {
        }
    }
}