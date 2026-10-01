using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class TranslationHistoryService
{
    private static readonly string HistoryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WOJD Localization Studio");

    private static readonly string HistoryPath = Path.Combine(
        HistoryDirectory,
        "translation-history.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task AppendSavedChangesAsync(
        LocalizationDocument document,
        IEnumerable<LocalizationEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var changed = entries
            .Where(entry => !string.Equals(
                entry.LastSavedTranslation,
                entry.Translation,
                StringComparison.Ordinal))
            .ToList();

        if (changed.Count == 0)
            return;

        Directory.CreateDirectory(HistoryDirectory);

        await using var stream = new FileStream(
            HistoryPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            useAsync: true);

        await using var writer = new StreamWriter(stream);

        foreach (var entry in changed)
        {
            var record = new TranslationHistoryRecord
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                FilePath = document.FilePath,
                FileName = document.FileName,
                Namespace = entry.Namespace,
                Key = entry.Key,
                Source = entry.Source,
                Before = entry.LastSavedTranslation,
                After = entry.Translation
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async Task<IReadOnlyList<TranslationHistoryRecord>> LoadForEntryAsync(
        LocalizationDocument document,
        LocalizationEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(HistoryPath))
            return Array.Empty<TranslationHistoryRecord>();

        var result = new List<TranslationHistoryRecord>();

        using var stream = new FileStream(
            HistoryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            4096,
            useAsync: true);

        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonSerializer.Deserialize<TranslationHistoryRecord>(line);
                if (record is null)
                    continue;

                if (string.Equals(record.FilePath, document.FilePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.Namespace, entry.Namespace, StringComparison.Ordinal) &&
                    string.Equals(record.Key, entry.Key, StringComparison.Ordinal))
                {
                    result.Add(record);
                }
            }
            catch (JsonException)
            {
                // Повреждённая строка истории не должна ломать редактор.
            }
        }

        return result
            .OrderByDescending(record => record.TimestampUtc)
            .ToList();
    }
}

public sealed class TranslationHistoryRecord
{
    public DateTimeOffset TimestampUtc { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;

    public string LocalTimestamp => TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}
