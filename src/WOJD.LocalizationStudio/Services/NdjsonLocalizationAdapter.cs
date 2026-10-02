using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class NdjsonLocalizationAdapter : ILocalizationFileAdapter
{
    private static readonly string[] KeyFields = ["key", "Key", "id", "Id", "name", "Name"];
    private static readonly string[] OriginalFields = ["original", "Original", "source", "Source", "cn", "CN", "zh", "ZH", "text", "Text"];
    private static readonly string[] TranslationFields = ["translation", "Translation", "target", "Target", "ru", "RU", "value", "Value"];

    public bool CanOpen(string path)
        => string.Equals(Path.GetExtension(path), ".ndjson", StringComparison.OrdinalIgnoreCase)
           || string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase);

    public async Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var document = new LocalizationDocument { FilePath = path };
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);

        string? line;
        var index = 1;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject obj;
            try
            {
                obj = JsonNode.Parse(line)?.AsObject() ?? new JsonObject();
            }
            catch (JsonException)
            {
                continue;
            }

            var key = ReadString(obj, KeyFields) ?? $"row_{index}";
            var original = ReadString(obj, OriginalFields) ?? string.Empty;
            var translationField = TranslationFields.FirstOrDefault(obj.ContainsKey) ?? "translation";
            var translation = ReadString(obj, [translationField]) ?? string.Empty;

            var entry = new LocalizationEntry
            {
                Index = index++,
                Key = key,
                Original = original,
                TranslationField = translationField,
                RawObject = obj
            };
            entry.InitializeSavedTranslation(translation);
            document.Entries.Add(entry);
        }

        return document;
    }

    public async Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default)
    {
        var tempPath = document.FilePath + ".tmp";
        await using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        foreach (var entry in document.Entries)
        {
            entry.RawObject[entry.TranslationField] = entry.Translation;
            var json = entry.RawObject.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
        File.Move(tempPath, document.FilePath, true);
        foreach (var entry in document.Entries) entry.MarkSaved();
    }

    private static string? ReadString(JsonObject obj, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var node) || node is null) continue;
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return node.ToJsonString().Trim('"');
        }
        return null;
    }
}
