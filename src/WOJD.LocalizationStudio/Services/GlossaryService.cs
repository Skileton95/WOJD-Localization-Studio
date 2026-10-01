using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class GlossaryService
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WOJD Localization Studio");

    private static readonly string GlossaryPath = Path.Combine(DataDirectory, "glossary.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ObservableCollection<GlossaryEntry> Entries { get; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Entries.Clear();

        if (!File.Exists(GlossaryPath))
            return;

        await using var stream = File.OpenRead(GlossaryPath);
        var entries = await JsonSerializer.DeserializeAsync<List<GlossaryEntry>>(
            stream,
            JsonOptions,
            cancellationToken);

        if (entries is null)
            return;

        foreach (var entry in entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
                     .OrderBy(entry => entry.Source, StringComparer.Ordinal))
            Entries.Add(entry);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DataDirectory);

        var tempPath = GlossaryPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                Entries.ToList(),
                JsonOptions,
                cancellationToken);
        }

        File.Move(tempPath, GlossaryPath, true);
    }

    public IReadOnlyList<GlossaryEntry> FindMatches(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<GlossaryEntry>();

        return Entries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Source) &&
                source.Contains(entry.Source, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Source.Length)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ToList();
    }

    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            Entries.ToList(),
            JsonOptions,
            cancellationToken);
    }

    public async Task<int> ImportAsync(
        string path,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var imported = await JsonSerializer.DeserializeAsync<List<GlossaryEntry>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        imported = imported
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Source) &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            .ToList();

        if (replace)
            Entries.Clear();

        var bySource = Entries.ToDictionary(
            entry => entry.Source,
            StringComparer.Ordinal);

        var changed = 0;

        foreach (var entry in imported)
        {
            if (bySource.TryGetValue(entry.Source, out var existing))
            {
                existing.Translation = entry.Translation;
                existing.Note = entry.Note;
                existing.IsLocked = entry.IsLocked;
                changed++;
                continue;
            }

            if (entry.Id == Guid.Empty)
                entry.Id = Guid.NewGuid();

            Entries.Add(entry);
            bySource[entry.Source] = entry;
            changed++;
        }

        await SaveAsync(cancellationToken);
        return changed;
    }
}
