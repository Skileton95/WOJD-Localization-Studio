using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class NdjsonExportService
{
    public static async Task ExportAsync(
        IEnumerable<LocalizationEntry> entries,
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            useAsync: true);

        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            256 * 1024);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonObject obj;

            try
            {
                obj =
                    JsonNode.Parse(entry.RawLine)?.AsObject()
                    ?? new JsonObject();
            }
            catch (JsonException)
            {
                obj = new JsonObject
                {
                    ["namespace"] = entry.Namespace,
                    ["key"] = entry.Key,
                    ["source"] = entry.Original
                };
            }

            obj[entry.TranslationField] =
                entry.Translation;

            var line =
                obj.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

            await writer.WriteLineAsync(
                line.AsMemory(),
                cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }
}
