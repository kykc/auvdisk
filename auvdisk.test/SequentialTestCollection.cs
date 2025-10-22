namespace auvdisk.test
{
    [CollectionDefinition("Sequential")]
    public class DatabaseCollection : ICollectionFixture<TestFixture>
    {
    }

    public class TestFixture : IDisposable
    {
        public TestFixture()
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();
        }

        public void Dispose()
        {
        }
    }
}