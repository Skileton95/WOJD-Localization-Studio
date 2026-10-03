using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record SearchIndexHeader(int Schema, string Hash);
public sealed record SearchIndexRow(int Index, string Namespace, string Key, string Original, string Translation);
public static class ProjectSearchIndexService
{
    public static async Task<string> EnsureAsync(string path, CancellationToken cancellation, IProgress<FileOperationProgress>? progress)
    {
        var directory = Path.Combine(WorkspaceStateService.StorageDirectory, "search-index"); Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, AnnotationStore.SourceHash(Path.GetFullPath(path)) + ".jsonl.gz");
        var hash = await Task.Run(() => FileSafetyService.Hash(path), cancellation);
        try
        {
            using var stream = File.OpenRead(cache); using var zip = new GZipStream(stream, CompressionMode.Decompress); using var reader = new StreamReader(zip);
            var header = JsonSerializer.Deserialize<SearchIndexHeader>(reader.ReadLine() ?? "");
            if (header is { Schema: 1 } && header.Hash == hash) return cache;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException) { }
        var document = await new NdjsonLocalizationAdapter().LoadAsync(path, cancellation, progress);
        var temp = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temp))
            await using (var zip = new GZipStream(stream, CompressionLevel.Fastest))
            await using (var writer = new StreamWriter(zip, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new SearchIndexHeader(1, document.DiskHash!)));
                foreach (var row in document.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new SearchIndexRow(row.Index, row.Namespace, row.Key, row.Original, row.Translation)));
                }
            }
            File.Move(temp, cache, true); return cache;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static IEnumerable<EntryLocation> Read(string cache, string file, CancellationToken cancellation)
    {
        using var stream = File.OpenRead(cache); using var zip = new GZipStream(stream, CompressionMode.Decompress); using var reader = new StreamReader(zip);
        _ = reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            cancellation.ThrowIfCancellationRequested();
            var row = JsonSerializer.Deserialize<SearchIndexRow>(line) ?? throw new InvalidDataException("Повреждён индекс поиска.");
            var entry = new LocalizationEntry { Index = row.Index, Namespace = row.Namespace, Key = row.Key, Original = row.Original };
            entry.InitializeSavedTranslation(row.Translation); yield return new(file, entry);
        }
    }
}