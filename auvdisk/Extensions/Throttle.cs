namespace auvdisk.Extensions
{
    public class Throttle<T>(Action<T> callback, TimeSpan interval)
    {
        private readonly object _lock = new();
        private DateTime _lastCallTime = DateTime.MinValue;

        public bool Call(T arg)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _lastCallTime > interval)
                {
                    _lastCallTime = now;
                    callback(arg);
                    return true;
                }
            }

            return false;
        }
    }
}
