#if WINDOWS
using Alphaleonis.Win32.Vss;
using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.Interop.Win32.Vss
{
    public class Backup : IDisposable
    {
        private readonly IVssBackupComponents _backup;
        private readonly Snapshot _snap;

        private Backup(string volumeName)
        {
            volumeName = volumeName switch 
            {
                [var l] => $"{l}:\\",
                [var l, _] => $"{l}:\\",
                _ => volumeName
            };
            
            try
            {
                IVssFactory vss = VssFactoryProvider.Default.GetVssFactory();

                _backup = vss.CreateVssBackupComponents();
                _backup.InitializeForBackup(null);
                _backup.GatherWriterMetadata();
                // Discovery
                _backup.FreeWriterMetadata();

                _snap = new Snapshot(_backup);
                _snap.AddVolume(Path.GetPathRoot(volumeName)!);
                PreBackup();
            }
            catch (Exception ex) when (Program.ExceptionFilter(ex))
            {
                _backup?.AbortBackup();
                Dispose();
                throw;
            }
        }

        public static Flow<Backup> Make(string volumeName, ILog logger)
        {
            return Flows.Val(None.Value)
                .HandleAll()
                .Map(_ => new Backup(volumeName))
                .PopHandler();
        }

        public void Dispose()
        {
            try
            {
                _backup?.BackupComplete();
            }
            // Not sure why, but this throws a VSS_BAD_STATE on XP and W2K3.
            // Per some forum posts about this, I'm just ignoring it.
            catch (VssBadStateException) { }

            _snap?.Dispose();
            _backup?.Dispose();
        }

        public string Root => _snap.Root;

        private void PreBackup()
        {
            _backup.SetBackupState(false, true, VssBackupType.Full, false);
            _backup.PrepareForBackup();
            _snap.Copy();
        }
    }
}
#endif