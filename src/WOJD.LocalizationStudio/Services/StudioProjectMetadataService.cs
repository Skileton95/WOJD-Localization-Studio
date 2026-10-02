using System.IO;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class StudioProjectMetadata
{
    public HashSet<string> FavoriteNamespaces { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, EntryNote> Notes { get; set; } =
        new(StringComparer.Ordinal);

    public List<GlossaryTerm> Glossary { get; set; } = [];

    public List<AssignmentRecord> Assignments { get; set; } = [];

    public List<SavedSmartFilter> SavedFilters { get; set; } = [];
}

public sealed record EntryNote(
    string Text,
    string State = "Черновик",
    string? Author = null,
    DateTime? UpdatedUtc = null);

public sealed record GlossaryTerm(
    string Source,
    string Translation,
    string Category = "Общее",
    string Requirement = "Обязательно",
    string? Comment = null);

public sealed record AssignmentRecord(
    string Namespace,
    string Assignee,
    string State = "Черновик");

public sealed record SavedSmartFilter(
    string Name,
    bool Untranslated = false,
    bool QaErrors = false,
    bool EmptySource = false,
    bool ContainsCjk = false,
    bool HasPlaceholders = false,
    bool LongTranslation = false,
    string? Namespace = null);

public static class StudioProjectMetadataService
{
    public static StudioProjectMetadata Load(string rootPath)
    {
        try
        {
            var path =
                GetMetadataPath(rootPath);

            if (!File.Exists(path))
                return new StudioProjectMetadata();

            return JsonSerializer.Deserialize<StudioProjectMetadata>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new StudioProjectMetadata();
        }
        catch
        {
            return new StudioProjectMetadata();
        }
    }

    public static void Save(
        string rootPath,
        StudioProjectMetadata metadata)
    {
        var folder =
            Path.Combine(
                rootPath,
                ".wojd-studio");

        Directory.CreateDirectory(folder);

        File.WriteAllText(
            GetMetadataPath(rootPath),
            JsonSerializer.Serialize(
                metadata,
                JsonOptions));
    }

    public static string EntryId(
        string filePath,
        string ns,
        string key)
        => $"{Path.GetFullPath(filePath)}\u001F{ns}\u001F{key}";

    private static string GetMetadataPath(string rootPath)
        => Path.Combine(
            Path.GetFullPath(rootPath),
            ".wojd-studio",
            "metadata.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
}
