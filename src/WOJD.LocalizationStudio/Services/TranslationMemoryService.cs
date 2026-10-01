using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class TranslationMemoryService
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WOJD Localization Studio");

    private static readonly string MemoryPath = Path.Combine(
        DataDirectory,
        "translation-memory.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly List<TranslationMemoryEntry> _entries = [];

    public IReadOnlyList<TranslationMemoryEntry> Entries => _entries;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _entries.Clear();

        if (!File.Exists(MemoryPath))
            return;

        await using var stream = File.OpenRead(MemoryPath);
        var loaded = await JsonSerializer.DeserializeAsync<List<TranslationMemoryEntry>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        _entries.AddRange(
            loaded
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Source) &&
                    !string.IsNullOrWhiteSpace(entry.Translation))
                .OrderByDescending(entry => entry.UpdatedUtc));
    }

    public async Task RememberDocumentAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        var index = _entries
            .GroupBy(
                item => BuildKey(item.Source, item.Namespace),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.UpdatedUtc)
                    .First(),
                StringComparer.Ordinal);

        foreach (var entry in document.Entries.Where(entry =>
                     !string.IsNullOrWhiteSpace(entry.Source) &&
                     !string.IsNullOrWhiteSpace(entry.Translation)))
        {
            var key = BuildKey(entry.Source, entry.Namespace);
            index.TryGetValue(key, out var memory);

            if (memory is null)
            {
                memory = new TranslationMemoryEntry
                {
                    Source = entry.Source,
                    Translation = entry.Translation,
                    Namespace = entry.Namespace,
                    Key = entry.Key,
                    FileName = document.FileName,
                    UpdatedUtc = now,
                    UseCount = 1,
                    History =
                    [
                        new TranslationMemoryVersion
                        {
                            Translation = entry.Translation,
                            UpdatedUtc = now,
                            FileName = document.FileName,
                            Key = entry.Key
                        }
                    ]
                };

                _entries.Add(memory);
                index[key] = memory;
                changed = true;
                continue;
            }

            memory.Key = entry.Key;
            memory.FileName = document.FileName;
            memory.UseCount = Math.Max(1, memory.UseCount + 1);

            if (!string.Equals(
                    memory.Translation,
                    entry.Translation,
                    StringComparison.Ordinal))
            {
                memory.Translation = entry.Translation;
                memory.UpdatedUtc = now;
                memory.History ??= [];

                memory.History.Add(new TranslationMemoryVersion
                {
                    Translation = entry.Translation,
                    UpdatedUtc = now,
                    FileName = document.FileName,
                    Key = entry.Key
                });

                if (memory.History.Count > 20)
                    memory.History = memory.History
                        .OrderByDescending(item => item.UpdatedUtc)
                        .Take(20)
                        .OrderBy(item => item.UpdatedUtc)
                        .ToList();

                changed = true;
            }
        }

        if (changed)
            await SaveAsync(cancellationToken);
    }

    private static string BuildKey(string source, string entryNamespace) =>
        $"{entryNamespace}\u001f{source}";

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DataDirectory);

        var tempPath = MemoryPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                _entries
                    .OrderByDescending(entry => entry.UpdatedUtc)
                    .ToList(),
                JsonOptions,
                cancellationToken);
        }

        File.Move(tempPath, MemoryPath, true);
    }
}

public sealed class TranslationMemoryEntry
{
    public string Source { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
    public int UseCount { get; set; }
    public List<TranslationMemoryVersion> History { get; set; } = [];
}

public sealed class TranslationMemoryVersion
{
    public string Translation { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
