#if WINDOWS
using Alphaleonis.Win32.Vss;

namespace auvdisk.Interop.Win32.Vss
{
    class Snapshot : IDisposable
    {
        private readonly IVssBackupComponents _backup;
        private readonly Guid _setId;
        private VssSnapshotProperties? _props;
        private Guid? _snapId;

        public Snapshot(IVssBackupComponents backup)
        {
            try
            {
                _backup = backup;
                _setId = backup.StartSnapshotSet();
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }
        public void Dispose()
        {
            try { Delete(); } catch { }
        }

        public void AddVolume(string volumeName)
        {
            if (_backup.IsVolumeSupported(volumeName))
            {
                _snapId = _backup.AddToSnapshotSet(volumeName);
            }
            else
            {
                throw new VssVolumeNotSupportedException(volumeName);
            }
        }

        public void Copy()
        {
            _backup.DoSnapshotSet();
        }
        public void Delete()
        {
            _backup.DeleteSnapshotSet(_setId, false);
        }

        public string Root
        {
            get
            {
                _props ??= _backup.GetSnapshotProperties(_snapId!.Value);
                return _props.SnapshotDeviceObject;
            }
        }
    }
}
#endif