using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record SnapshotInfo(
    string Path,
    DateTimeOffset Created,
    string Label,
    int EntryCount,
    long Size);

public sealed record SnapshotRestoreResult(
    int Restored,
    int Skipped,
    string Message);

public static class SnapshotService
{
    private const int MaxSnapshotsPerFile = 10;

    public static SnapshotInfo CreateSnapshot(
        LocalizationDocument document,
        string label)
    {
        var sourcePath = Path.GetFullPath(document.FilePath);
        var directory = GetSnapshotDirectory(sourcePath);
        Directory.CreateDirectory(directory);

        var safeLabel = Sanitize(label);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff");
        var fileName =
            $"{Path.GetFileName(sourcePath)}.{stamp}.{safeLabel}.snapshot.gz";
        var path = Path.Combine(directory, fileName);

        using (var stream = new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   1024 * 1024,
                   FileOptions.SequentialScan))
        using (var gzip = new GZipStream(
                   stream,
                   CompressionLevel.Fastest,
                   leaveOpen: false))
        using (var writer = new StreamWriter(
                   gzip,
                   new UTF8Encoding(false),
                   1024 * 1024))
        {
            writer.WriteLine(
                JsonSerializer.Serialize(
                    new SnapshotHeader(
                        sourcePath,
                        DateTimeOffset.UtcNow,
                        label,
                        document.Entries.Count)));

            foreach (var entry in document.Entries)
            {
                writer.WriteLine(
                    JsonSerializer.Serialize(
                        new SnapshotEntry(
                            entry.Index,
                            entry.Namespace,
                            entry.Key,
                            entry.Translation)));
            }
        }

        TrimOldSnapshots(sourcePath);

        return new SnapshotInfo(
            path,
            DateTimeOffset.Now,
            label,
            document.Entries.Count,
            new FileInfo(path).Length);
    }

    public static IReadOnlyList<SnapshotInfo> ListSnapshots(
        string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = GetSnapshotDirectory(fullPath);

        if (!Directory.Exists(directory))
            return [];

        var prefix = Path.GetFileName(fullPath) + ".";

        return Directory
            .EnumerateFiles(directory, "*.snapshot.gz")
            .Where(x =>
                Path.GetFileName(x)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(ReadInfo)
            .Where(x => x is not null)
            .Cast<SnapshotInfo>()
            .OrderByDescending(x => x.Created)
            .ToList();
    }

    public static SnapshotRestoreResult RestoreSnapshot(
        LocalizationDocument document,
        string snapshotPath)
    {
        using var stream = new FileStream(
            snapshotPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        using var gzip = new GZipStream(
            stream,
            CompressionMode.Decompress);
        using var reader = new StreamReader(
            gzip,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024);

        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException("Снимок повреждён: отсутствует заголовок.");

        var header = JsonSerializer.Deserialize<SnapshotHeader>(headerLine)
            ?? throw new InvalidDataException("Снимок повреждён: неверный заголовок.");

        if (header.EntryCount != document.Entries.Count)
        {
            throw new InvalidOperationException(
                $"Снимок содержит {header.EntryCount:N0} строк, а текущий файл — {document.Entries.Count:N0}. " +
                "Восстановление отменено, чтобы не применить снимок к другой версии файла.");
        }

        var restored = 0;
        var skipped = 0;

        using (EntryHistoryService.BeginOperation("Восстановление снимка"))
        {
            for (var i = 0; i < document.Entries.Count; i++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    throw new InvalidDataException(
                        $"Снимок обрывается на строке {i + 1:N0}.");
                }

                var saved = JsonSerializer.Deserialize<SnapshotEntry>(line)
                    ?? throw new InvalidDataException(
                        $"Снимок повреждён на строке {i + 1:N0}.");

                var current = document.Entries[i];

                if (saved.Index != current.Index ||
                    !string.Equals(saved.Namespace, current.Namespace, StringComparison.Ordinal) ||
                    !string.Equals(saved.Key, current.Key, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                if (!string.Equals(
                        current.Translation,
                        saved.Translation,
                        StringComparison.Ordinal))
                {
                    current.Translation = saved.Translation;
                    restored++;
                }
            }
        }

        return new SnapshotRestoreResult(
            restored,
            skipped,
            $"Восстановлено строк: {restored:N0}. Пропущено несовпавших: {skipped:N0}.");
    }

    private static SnapshotInfo? ReadInfo(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var line = reader.ReadLine();
            var header = string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<SnapshotHeader>(line);

            if (header is null)
                return null;

            return new SnapshotInfo(
                path,
                header.CreatedUtc.ToLocalTime(),
                header.Label,
                header.EntryCount,
                new FileInfo(path).Length);
        }
        catch
        {
            return null;
        }
    }

    private static void TrimOldSnapshots(string filePath)
    {
        foreach (var snapshot in ListSnapshots(filePath).Skip(MaxSnapshotsPerFile))
        {
            try
            {
                File.Delete(snapshot.Path);
            }
            catch
            {
                // Очистка старых снимков не должна мешать работе редактора.
            }
        }
    }

    private static string GetSnapshotDirectory(string filePath)
        => Path.Combine(
            Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
            ".localization-snapshots");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(
            (value ?? "snapshot")
                .Select(c => invalid.Contains(c) ? '_' : c)
                .ToArray());

        return string.IsNullOrWhiteSpace(result)
            ? "snapshot"
            : result.Replace(' ', '-');
    }

    private sealed record SnapshotHeader(
        string FilePath,
        DateTimeOffset CreatedUtc,
        string Label,
        int EntryCount);

    private sealed record SnapshotEntry(
        int Index,
        string Namespace,
        string Key,
        string Translation);
}
