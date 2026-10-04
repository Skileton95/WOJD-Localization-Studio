using System.Runtime.CompilerServices;
using System.Windows;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio.Services;

public enum ExternalFileGuardResult
{
    ContinueSave,
    ReloadedFromDisk
}

public sealed class ExternalFileSaveCanceledException : IOException
{
    public ExternalFileSaveCanceledException()
        : base("Сохранение отменено: файл был изменён другой программой.")
    {
    }
}

public static class ExternalFileGuardService
{
    private static readonly ConditionalWeakTable<LocalizationDocument, StampHolder>
        Stamps = new();

    public static void TrackLoaded(LocalizationDocument document)
    {
        var holder = Stamps.GetOrCreateValue(document);
        holder.Stamp = ExternalFileChangeService.Capture(document.FilePath);
    }

    public static void MarkSaved(LocalizationDocument document)
        => TrackLoaded(document);

    public static async Task<ExternalFileGuardResult> ResolveBeforeSaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var holder = Stamps.GetOrCreateValue(document);

        if (holder.Stamp == default)
        {
            holder.Stamp = ExternalFileChangeService.Capture(document.FilePath);
            return ExternalFileGuardResult.ContinueSave;
        }

        var current = ExternalFileChangeService.Capture(document.FilePath);

        if (!HasChanged(holder.Stamp, current))
            return ExternalFileGuardResult.ContinueSave;

        cancellationToken.ThrowIfCancellationRequested();

        var diskDocument = await new NdjsonLocalizationAdapter()
            .LoadAsync(document.FilePath, cancellationToken);

        ExternalFileConflictDecision decision = ExternalFileConflictDecision.Cancel;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ExternalFileConflictWindow(
                document,
                diskDocument,
                holder.Stamp,
                current)
            {
                Owner = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(x => x.IsActive)
                    ?? Application.Current.MainWindow
            };

            dialog.ShowDialog();
            decision = dialog.Decision;
        });

        cancellationToken.ThrowIfCancellationRequested();

        switch (decision)
        {
            case ExternalFileConflictDecision.Overwrite:
                return ExternalFileGuardResult.ContinueSave;

            case ExternalFileConflictDecision.Reload:
                ApplyDiskState(document, diskDocument);
                holder.Stamp = current;
                ProjectHistoryService.Record(
                    document.FilePath,
                    "Перезагрузка внешних изменений",
                    document.Entries.Count,
                    "Локальные несохранённые изменения отброшены; загружена версия с диска.");
                return ExternalFileGuardResult.ReloadedFromDisk;

            default:
                throw new ExternalFileSaveCanceledException();
        }
    }

    private static void ApplyDiskState(
        LocalizationDocument target,
        LocalizationDocument disk)
    {
        if (target.Entries.Count != disk.Entries.Count)
        {
            throw new InvalidOperationException(
                "Структура файла изменилась; автоматическая перезагрузка невозможна.");
        }

        for (var i = 0; i < target.Entries.Count; i++)
        {
            var current = target.Entries[i];
            var saved = disk.Entries[i];

            if (current.Index != saved.Index ||
                !string.Equals(current.Namespace, saved.Namespace, StringComparison.Ordinal) ||
                !string.Equals(current.Key, saved.Key, StringComparison.Ordinal) ||
                !string.Equals(current.Original, saved.Original, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Структура файла изменилась около строки {i + 1:N0}; автоматическая перезагрузка невозможна.");
            }

            current.RawLine = saved.RawLine;
            current.InitializeSavedTranslation(saved.Translation);
        }
    }

    private static bool HasChanged(FileStamp baseline, FileStamp current)
    {
        if (baseline.Exists != current.Exists)
            return true;

        if (!baseline.Exists)
            return false;

        return baseline.Length != current.Length ||
               baseline.LastWriteUtc != current.LastWriteUtc;
    }

    private sealed class StampHolder
    {
        public FileStamp Stamp { get; set; }
    }
}
