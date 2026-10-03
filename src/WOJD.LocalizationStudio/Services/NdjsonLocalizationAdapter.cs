using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;

public sealed class NdjsonLocalizationAdapter : ILocalizationFileAdapter
{
    private static readonly string[] NamespaceFields = ["namespace", "Namespace"];
    private static readonly string[] KeyFields = ["key", "Key", "id", "Id", "name", "Name"];
    private static readonly string[] OriginalFields = ["original", "Original", "source", "Source", "cn", "CN", "zh", "ZH", "text", "Text"];
    private static readonly string[] TranslationFields = ["translated", "Translated", "translation", "Translation", "target", "Target", "ru", "RU", "value", "Value"];
    public bool CanOpen(string path) => Path.GetExtension(path).Equals(".ndjson", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase);
    public Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null)
        => Task.Run(() => LoadCore(path, cancellationToken, progress), cancellationToken);
    private static LocalizationDocument LoadCore(string path, CancellationToken cancellation, IProgress<FileOperationProgress>? progress)
    {
        var document = new LocalizationDocument { FilePath = path };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var prefix = new byte[Math.Min(stream.Length, 4)]; stream.ReadExactly(prefix); stream.Position = 0;
        var header = 0; Encoding encoding = new UTF8Encoding(false, true);
        if (prefix.Length >= 4 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 254 && prefix[3] == 255) { encoding = new UTF32Encoding(true, true, true); header = 4; }
        else if (prefix.Length >= 4 && prefix[0] == 255 && prefix[1] == 254 && prefix[2] == 0 && prefix[3] == 0) { encoding = new UTF32Encoding(false, true, true); header = 4; }
        else if (prefix.Length >= 3 && prefix[0] == 239 && prefix[1] == 187 && prefix[2] == 191) { encoding = new UTF8Encoding(true, true); header = 3; }
        else if (prefix.Length >= 2 && prefix[0] == 255 && prefix[1] == 254) { encoding = new UnicodeEncoding(false, true, true); header = 2; }
        else if (prefix.Length >= 2 && prefix[0] == 254 && prefix[1] == 255) { encoding = new UnicodeEncoding(true, true, true); header = 2; }
        stream.Position = header;
        using var reader = new StreamReader(stream, encoding, false, 1024 * 1024);
        document.Encoding = encoding;
        document.NewLine = Environment.NewLine;
        var scanned = 0;
        while (reader.Read() is var ch && ch >= 0)
        {
            if ((++scanned & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            if (ch == '\n') { document.NewLine = "\n"; break; }
            if (ch == '\r') { document.NewLine = reader.Peek() == '\n' ? "\r\n" : "\r"; break; }
        }
        reader.DiscardBufferedData(); stream.Position = header;
        var physical = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellation.ThrowIfCancellationRequested(); physical++;
            if (string.IsNullOrWhiteSpace(line)) { document.PreservedLines.Add(new(physical, line)); continue; }
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Ожидается JSON-объект.");
                var translationField = TranslationFields.FirstOrDefault(field => root.TryGetProperty(field, out _)) ?? "translation";
                var entry = new LocalizationEntry
                {
                    Index = document.Entries.Count + 1, LineNumber = physical,
                    Namespace = ReadString(root, NamespaceFields) ?? "", Key = ReadString(root, KeyFields) ?? $"row_{document.Entries.Count + 1}",
                    Original = ReadString(root, OriginalFields) ?? "", TranslationField = translationField, RawLine = line
                };
                entry.InitializeSavedTranslation(ReadString(root, [translationField]) ?? "");
                document.Entries.Add(entry);
            }
            catch (JsonException e)
            {
                document.PreservedLines.Add(new(physical, line));
                document.LoadIssues.Add(new(physical, e.Message));
            }
            if ((physical & 4095) == 0) progress?.Report(new(Math.Min(99, 100.0 * stream.Position / Math.Max(1, stream.Length)), document.Entries.Count, "Открытие"));
        }

        document.DiskHash = FileSafetyService.Hash(path);
        if (document.LoadIssues.Count > 0) IssueLogService.Record($"Открыт {path}; повреждённых строк: {document.LoadIssues.Count}");
        progress?.Report(new(100, document.Entries.Count, "Открытие"));
        return document;
    }
    public async Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null)
    {
        FileSafetyService.CheckUnchanged(document);
        var tempPath = document.FilePath + ".wojd-" + Guid.NewGuid().ToString("N") + ".tmp";
        var changes = new List<(LocalizationEntry Entry, string RawLine)>();
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                await using var writer = new StreamWriter(stream, document.Encoding, 1024 * 1024, leaveOpen: true) { NewLine = document.NewLine };
                var preserved = 0; var written = 0;
                foreach (var entry in document.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (preserved < document.PreservedLines.Count && document.PreservedLines[preserved].Line < entry.LineNumber)
                        await writer.WriteLineAsync(document.PreservedLines[preserved++].Text.AsMemory(), cancellationToken);
                    var line = entry.Status == TranslationStatus.Modified ? SerializeEntry(entry) : entry.RawLine;
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                    if (entry.Status == TranslationStatus.Modified) changes.Add((entry, line));
                    if ((++written & 4095) == 0) progress?.Report(new(100.0 * written / Math.Max(1, document.Entries.Count), written, "Сохранение"));
                }
                while (preserved < document.PreservedLines.Count)
                    await writer.WriteLineAsync(document.PreservedLines[preserved++].Text.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken); stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested(); FileSafetyService.CheckUnchanged(document);
            File.Move(tempPath, document.FilePath, true);
            document.DiskHash = FileSafetyService.Hash(document.FilePath);
            foreach (var (entry, line) in changes) entry.MarkSaved(line);
            progress?.Report(new(100, document.Entries.Count, "Сохранение"));
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }
    public static string SerializeEntry(LocalizationEntry entry)
    {
        var obj = JsonNode.Parse(entry.RawLine)?.AsObject() ?? throw new JsonException("Запись потеряла исходный JSON.");
        obj[entry.TranslationField] = entry.Translation;
        if (entry.NamespaceModified) obj[NamespaceFields.FirstOrDefault(obj.ContainsKey) ?? "namespace"] = entry.Namespace;
        if (entry.KeyModified) obj[KeyFields.FirstOrDefault(obj.ContainsKey) ?? "key"] = entry.Key;
        if (entry.OriginalModified) obj[OriginalFields.FirstOrDefault(obj.ContainsKey) ?? "source"] = entry.Original;
        return obj.ToJsonString();
    }
    private static string? ReadString(JsonElement root, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value))
                return value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Null => null, _ => value.GetRawText().Trim('"') };
        return null;
    }
}