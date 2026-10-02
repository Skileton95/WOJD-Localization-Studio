using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class NdjsonLocalizationAdapter : ILocalizationFileAdapter
{
    private static readonly string[] NamespaceFields =
        ["namespace", "Namespace"];

    private static readonly string[] KeyFields =
        ["key", "Key", "id", "Id", "name", "Name"];

    private static readonly string[] OriginalFields =
        ["original", "Original", "source", "Source", "cn", "CN", "zh", "ZH", "text", "Text"];

    private static readonly string[] TranslationFields =
        ["translated", "Translated", "translation", "Translation", "target", "Target", "ru", "RU", "value", "Value"];

    public bool CanOpen(string path)
        => string.Equals(
               Path.GetExtension(path),
               ".ndjson",
               StringComparison.OrdinalIgnoreCase)
           || string.Equals(
               Path.GetExtension(path),
               ".jsonl",
               StringComparison.OrdinalIgnoreCase);

    public Task<LocalizationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        // JSON-разбор выполняется вне UI-потока.
        return Task.Run(
            () => LoadCore(path, cancellationToken),
            cancellationToken);
    }

    private static LocalizationDocument LoadCore(
        string path,
        CancellationToken cancellationToken)
    {
        var document = new LocalizationDocument
        {
            FilePath = path
        };

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.SequentialScan);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024);

        string? line;
        var index = 1;

        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                var entryNamespace =
                    ReadString(root, NamespaceFields)
                    ?? string.Empty;

                var key =
                    ReadString(root, KeyFields)
                    ?? $"row_{index}";

                var original =
                    ReadString(root, OriginalFields)
                    ?? string.Empty;

                var translationField =
                    TranslationFields.FirstOrDefault(
                        field => root.TryGetProperty(field, out _))
                    ?? "translation";

                var translation =
                    ReadString(root, [translationField])
                    ?? string.Empty;

                var entry = new LocalizationEntry
                {
                    Index = index++,
                    Namespace = entryNamespace,
                    Key = key,
                    Original = original,
                    TranslationField = translationField,
                    RawLine = line
                };

                entry.InitializeSavedTranslation(translation);
                document.Entries.Add(entry);
            }
            catch (JsonException)
            {
                // Невалидная NDJSON-строка пропускается,
                // как и в предыдущей версии редактора.
            }
        }

        return document;
    }

    public async Task SaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var tempPath = document.FilePath + ".tmp";

        await using var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            useAsync: true);

        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            bufferSize: 1024 * 1024);

        var changedEntries =
            new List<(LocalizationEntry Entry, string RawLine)>();

        foreach (var entry in document.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = entry.RawLine;

            if (entry.Status == TranslationStatus.Modified)
            {
                JsonObject obj;

                try
                {
                    obj =
                        JsonNode.Parse(entry.RawLine)?.AsObject()
                        ?? new JsonObject();
                }
                catch (JsonException)
                {
                    obj = new JsonObject();
                }

                obj[entry.TranslationField] = entry.Translation;

                line = obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

                changedEntries.Add((entry, line));
            }

            await writer.WriteLineAsync(
                line.AsMemory(),
                cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);

        File.Move(
            tempPath,
            document.FilePath,
            overwrite: true);

        foreach (var (entry, rawLine) in changedEntries)
            entry.MarkSaved(rawLine);
    }

    private static string? ReadString(
        JsonElement root,
        IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => value.GetRawText().Trim('"')
            };
        }

        return null;
    }
}
