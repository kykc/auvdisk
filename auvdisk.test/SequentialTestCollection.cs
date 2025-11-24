namespace auvdisk.test
{
    [CollectionDefinition("Sequential")]
    public class SequentialCollection : ICollectionFixture<TestFixture>
    {
    }

    public class TestFixture : IDisposable
    {
        public void Dispose()
        {
        }
    }
}