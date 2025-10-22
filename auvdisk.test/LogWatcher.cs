using Spectre.Console.Rendering;

namespace auvdisk.test
{
    public class LogWatcher : auvdisk.Log.ILog
    {
        private List<string> _log = new();
        private List<string> _warning = new();
        private List<string> _error = new();
        private List<string> _all = new();
        
        public void Log(IRenderable log)
        {
            // TODO: figure out implementation
        }

        public void Log(string log)
        {
            _log.Add(log);
            _all.Add(log);
        }

        public void Error(string error)
        {
            _error.Add(error);
            _all.Add(error);
        }

        public void Warning(string warning)
        {
            _warning.Add(warning);
            _all.Add(warning);
        }

        public Action<string> ToAction()
        {
            return Log;
        }

        public IEnumerable<string> GetAll()
        {
            return _all;
        }
    }
}