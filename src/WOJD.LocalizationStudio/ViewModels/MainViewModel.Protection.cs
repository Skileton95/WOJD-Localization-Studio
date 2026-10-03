using System.IO;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed record RecoveryConflict(string FilePath, DraftEntryState Entry, string Reason);
public sealed partial class MainViewModel
{
    private readonly List<DraftFileState> _blockedRecovery = [];
    public List<RecoveryConflict> RecoveryConflicts => _blockedRecovery.SelectMany(x => x.Entries.Select(e => new RecoveryConflict(x.FilePath, e, "Source не совпадает или строка отсутствует"))).ToList();
    public void RemoveRecoveryConflict(RecoveryConflict row)
    {
        foreach (var file in _blockedRecovery.Where(x => x.FilePath == row.FilePath)) file.Entries.Remove(row.Entry);
        _blockedRecovery.RemoveAll(x => x.Entries.Count == 0); PersistWorkspaceState(true);
    }
    public async Task<List<string>> DetectExternalChangesAsync()
    {
        var docs = OpenDocuments;
        return await Task.Run(() => docs.Where(d => !File.Exists(d.FilePath) ||
            ((File.GetLastWriteTimeUtc(d.FilePath) != d.DiskLastWriteUtc || new FileInfo(d.FilePath).Length != d.DiskLength) && FileSafetyService.Hash(d.FilePath) != d.DiskHash))
            .Select(d => d.FilePath).ToList());
    }
    public async Task ReloadProtectedAsync()
    {
        if (_activeSession is not { } session || IsBusy) return;
        IsBusy = true;
        try
        {
            if (session.HasUnsavedChanges) await DocumentSnapshotService.WriteAsync(session.Document, BackupService.NewBackupPath(session.Document.FilePath));
            await ReloadActiveFromDiskAsync(); SaveTransactionService.Record("reload", session.Document.FilePath, "Версия на диске загружена.");
        }
        finally { IsBusy = false; }
    }
    public async Task KeepMineAndSaveAsync()
    {
        if (_activeSession is not { } session || IsBusy) return;
        IsBusy = true;
        try
        {
            await DocumentSnapshotService.WriteAsync(session.Document, BackupService.NewBackupPath(session.Document.FilePath));
            session.Document.DiskHash = FileSafetyService.Hash(session.Document.FilePath);
            await SaveSessionAsync(session); SaveTransactionService.Record("keep-mine", session.Document.FilePath, "Внешняя версия сохранена в backup перед заменой.");
        }
        finally { IsBusy = false; }
    }
}