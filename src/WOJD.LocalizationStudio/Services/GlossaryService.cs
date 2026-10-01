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
                     .Where(entry =>
                         !string.IsNullOrWhiteSpace(entry.Source) &&
                         !string.IsNullOrWhiteSpace(entry.Translation))
                     .OrderByDescending(entry => entry.Priority)
                     .ThenByDescending(entry => entry.Source.Length)
                     .ThenBy(entry => entry.Source, StringComparer.Ordinal))
        {
            if (entry.Id == Guid.Empty)
                entry.Id = Guid.NewGuid();

            entry.AllowedTranslations ??= [];
            entry.NamespaceScopes ??= [];

            Entries.Add(entry);
        }
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

    public IReadOnlyList<GlossaryEntry> FindMatches(
        string source,
        string? entryNamespace = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<GlossaryEntry>();

        var candidates = Entries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Source) &&
                entry.AppliesToNamespace(entryNamespace) &&
                source.Contains(entry.Source, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Priority)
            .ThenByDescending(entry => entry.Source.Length)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count <= 1)
            return candidates;

        // Более приоритетный или более длинный термин подавляет вложенный
        // термин, если оба описывают один и тот же участок исходной строки.
        var accepted = new List<GlossaryEntry>();

        foreach (var candidate in candidates)
        {
            var candidateStart = source.IndexOf(candidate.Source, StringComparison.Ordinal);
            if (candidateStart < 0)
                continue;

            var candidateEnd = candidateStart + candidate.Source.Length;

            var shadowed = accepted.Any(existing =>
            {
                var existingStart = source.IndexOf(existing.Source, StringComparison.Ordinal);
                if (existingStart < 0)
                    return false;

                var existingEnd = existingStart + existing.Source.Length;
                var overlaps = candidateStart < existingEnd && existingStart < candidateEnd;

                if (!overlaps)
                    return false;

                if (existing.Priority > candidate.Priority)
                    return true;

                return existing.Priority == candidate.Priority &&
                       existing.Source.Length >= candidate.Source.Length;
            });

            if (!shadowed)
                accepted.Add(candidate);
        }

        return accepted;
    }

    public IReadOnlyList<GlossaryConflict> GetConflicts()
    {
        var result = new List<GlossaryConflict>();

        foreach (var group in Entries
                     .GroupBy(entry => entry.Source, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var entries = group.ToList();

            for (var i = 0; i < entries.Count; i++)
            {
                for (var j = i + 1; j < entries.Count; j++)
                {
                    var left = entries[i];
                    var right = entries[j];

                    if (!ScopesOverlap(left.NamespaceScopes, right.NamespaceScopes))
                        continue;

                    var leftAccepted = left.GetAcceptedTranslations();
                    var rightAccepted = right.GetAcceptedTranslations();
                    var sameTranslation = leftAccepted.Any(leftValue =>
                        rightAccepted.Contains(leftValue, StringComparer.OrdinalIgnoreCase));

                    if (!sameTranslation)
                    {
                        result.Add(new GlossaryConflict(
                            left,
                            right,
                            $"Одинаковый китайский термин «{group.Key}» имеет разные переводы в пересекающейся области Namespace."));
                    }
                }
            }
        }

        var ordered = Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var left = ordered[i];
                var right = ordered[j];

                if (string.Equals(left.Source, right.Source, StringComparison.Ordinal))
                    continue;

                if (!ScopesOverlap(left.NamespaceScopes, right.NamespaceScopes))
                    continue;

                var overlap =
                    left.Source.Contains(right.Source, StringComparison.Ordinal) ||
                    right.Source.Contains(left.Source, StringComparison.Ordinal);

                if (!overlap)
                    continue;

                if (left.Priority == right.Priority)
                {
                    result.Add(new GlossaryConflict(
                        left,
                        right,
                        $"Пересекающиеся термины «{left.Source}» и «{right.Source}» имеют одинаковый приоритет {left.Priority}."));
                }
            }
        }

        return result;
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

        var changed = 0;

        foreach (var entry in imported)
        {
            entry.AllowedTranslations ??= [];
            entry.NamespaceScopes ??= [];

            var existing = Entries.FirstOrDefault(current =>
                string.Equals(current.Source, entry.Source, StringComparison.Ordinal) &&
                ScopesEqual(current.NamespaceScopes, entry.NamespaceScopes));

            if (existing is not null)
            {
                existing.Translation = entry.Translation;
                existing.Note = entry.Note;
                existing.IsLocked = entry.IsLocked;
                existing.AllowedTranslations = entry.AllowedTranslations;
                existing.NamespaceScopes = entry.NamespaceScopes;
                existing.Priority = entry.Priority;
                changed++;
                continue;
            }

            if (entry.Id == Guid.Empty)
                entry.Id = Guid.NewGuid();

            Entries.Add(entry);
            changed++;
        }

        await SaveAsync(cancellationToken);
        return changed;
    }

    private static bool ScopesEqual(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool ScopesOverlap(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return true;

        return left.Any(value =>
            right.Contains(value, StringComparer.Ordinal));
    }
}

public sealed record GlossaryConflict(
    GlossaryEntry Left,
    GlossaryEntry Right,
    string Message);
