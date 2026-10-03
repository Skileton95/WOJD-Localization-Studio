using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public void RevertSelectedToSaved()
    {
        if (SelectedEntry is not { } row) return;
        ApplyBatch(new[] { (row, EntryField.Translation, row.Translation, row.SavedTranslation),
            (row, EntryField.Namespace, row.Namespace, row.SavedNamespace), (row, EntryField.Key, row.Key, row.SavedKey),
            (row, EntryField.Original, row.Original, row.SavedOriginal) });
    }
    public async Task ReloadActiveFromDiskAsync()
    {
        if (_activeSession is not { } session) return;
        var document = await _adapter.LoadAsync(session.Document.FilePath);
        ApplyProjectRole(document);
        foreach (var row in session.Document.Entries) { row.PropertyChanged -= Entry_PropertyChanged; _entrySessions.Remove(row); }
        session.Document = document; session.SelectedEntry = document.Entries.FirstOrDefault();
        session.UndoStack.Clear(); session.RedoStack.Clear();
        foreach (var row in document.Entries) { row.PropertyChanged += Entry_PropertyChanged; _entrySessions[row] = session; }
        RebuildStatusCache(session); session.Node.EntryCount = document.Entries.Count; session.Node.IsModified = false;
        _consistencyDirty = true;
        ActivateSession(session);
    }
    public async Task RestoreBackupAsync(string backupPath)
    {
        if (_activeSession is not { } session) return;
        var current = session.Document;
        if (current.IsReadOnly) throw new IOException("Исходник CN/EN открыт только для чтения.");
        if (!BackupService.List(current.FilePath).Any(x => x.Path == backupPath)) throw new IOException("Копия не относится к текущему файлу.");
        var old = await new NdjsonLocalizationAdapter().LoadAsync(backupPath);
        if (old.LoadIssues.Count > 0) throw new IOException("Повреждённая копия; восстановление заблокировано.");
        FileSafetyService.CheckUnchanged(current);
        BackupService.CreateBackup(current.FilePath);
        if (session.HasUnsavedChanges)
            await DocumentSnapshotService.WriteAsync(current, BackupService.NewBackupPath(current.FilePath));
        var restored = new LocalizationDocument { FilePath = current.FilePath, DiskHash = current.DiskHash, Encoding = old.Encoding, NewLine = old.NewLine, ProviderId = current.ProviderId, HasFinalNewLine = old.HasFinalNewLine };
        foreach (var ending in old.LineEndings) restored.LineEndings[ending.Key] = ending.Value;
        restored.Entries.AddRange(old.Entries); restored.PreservedLines.AddRange(old.PreservedLines);
        await _adapter.SaveAsync(restored);
        await ReloadActiveFromDiskAsync();
    }
}