namespace auvdisk.test
{
    class DisposableDummy : IDisposable
    {
        public bool Disposed { get; private set; } = false;

        public int DummyMember { get; private set; } = 42;

        public void Dispose()
        {
            if (Disposed)
            {
                throw new InvalidOperationException();
            }
            
            Disposed = true;
        }
    }
}