using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class NdjsonService
{
    private static readonly string[] NamespaceNames = ["namespace", "Namespace"];
    private static readonly string[] KeyNames = ["key", "Key", "id", "Id", "name", "Name"];
    private static readonly string[] SourceNames = ["source", "Source", "original", "Original", "zh", "zh_CN", "chinese", "Chinese", "text"];
    private static readonly string[] TranslationNames = ["translated", "Translated", "translation", "Translation", "ru", "ru_RU", "value", "Value", "target", "Target"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<List<LocalizationEntry>> LoadAsync(string path)
    {
        var result = new List<LocalizationEntry>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        var lineNo = 0;

        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonObject obj;
            try
            {
                if (JsonNode.Parse(line) is not JsonObject parsed) continue;
                obj = parsed;
            }
            catch (JsonException)
            {
                continue;
            }

            var namespaceValue = FirstString(obj, NamespaceNames) ?? string.Empty;
            var key = FirstString(obj, KeyNames) ?? $"line_{lineNo}";
            var source = FirstString(obj, SourceNames) ?? string.Empty;
            var translationName = TranslationNames.FirstOrDefault(obj.ContainsKey) ?? "translated";
            var translation = FirstString(obj, TranslationNames) ?? string.Empty;

            if (string.IsNullOrEmpty(source))
                source = FindChineseSource(obj, translationName) ?? string.Empty;

            var entry = new LocalizationEntry
            {
                LineNumber = lineNo,
                Namespace = namespaceValue,
                Key = key,
                Source = source,
                Raw = (JsonObject)obj.DeepClone(),
                TranslationPropertyName = translationName
            };
            entry.SetLoadedTranslation(translation);
            result.Add(entry);
        }

        return result;
    }

    public async Task SaveAsync(string path, IEnumerable<LocalizationEntry> entries)
    {
        var tempPath = path + ".wojd.tmp";

        try
        {
            await using (var writer = new StreamWriter(tempPath, false, new UTF8Encoding(false)))
            {
                foreach (var entry in entries.OrderBy(e => e.LineNumber))
                {
                    var obj = (JsonObject)entry.Raw.DeepClone();
                    obj[entry.TranslationPropertyName] = entry.Translation;
                    await writer.WriteLineAsync(obj.ToJsonString(WriteOptions)).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string? FirstString(JsonObject obj, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text))
                return text;
        }
        return null;
    }

    private static string? FindChineseSource(JsonObject obj, string translationName)
    {
        foreach (var pair in obj)
        {
            if (pair.Key.Equals(translationName, StringComparison.OrdinalIgnoreCase)) continue;
            if (NamespaceNames.Contains(pair.Key) || KeyNames.Contains(pair.Key)) continue;
            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && ContainsCjk(text))
                return text;
        }
        return null;
    }

    private static bool ContainsCjk(string value) =>
        value.Any(c => c is >= '\u3400' and <= '\u9FFF');
}
