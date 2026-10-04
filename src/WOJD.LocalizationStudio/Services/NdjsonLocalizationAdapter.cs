using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class NdjsonLocalizationAdapter : ILocalizationFileAdapter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);

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
                // Невалидная NDJSON-строка пропускается при открытии.
            }
        }

        return document;
    }

    public async Task SaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var targetPath = Path.GetFullPath(document.FilePath);
        var saveLock = SaveLocks.GetOrAdd(
            targetPath,
            static _ => new SemaphoreSlim(1, 1));

        await saveLock.WaitAsync(cancellationToken);

        try
        {
            await SaveCoreAsync(
                document,
                targetPath,
                cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static async Task SaveCoreAsync(
        LocalizationDocument document,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("Не удалось определить папку сохраняемого файла.");

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.save.tmp");

        var changedEntries =
            new List<(LocalizationEntry Entry, string RawLine)>();

        try
        {
            try
            {
                await WriteTemporaryFileAsync(
                    document,
                    tempPath,
                    changedEntries,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Не удалось создать или записать временный файл для «{Path.GetFileName(targetPath)}». " +
                    "Проверьте права доступа и свободное место на диске.",
                    ex);
            }

            await ValidateTemporaryFileAsync(
                tempPath,
                document.Entries.Count,
                cancellationToken);

            await ReplaceTargetWithRetryAsync(
                tempPath,
                targetPath,
                cancellationToken);

            foreach (var (entry, rawLine) in changedEntries)
                entry.MarkSaved(rawLine);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private static async Task WriteTemporaryFileAsync(
        LocalizationDocument document,
        string tempPath,
        ICollection<(LocalizationEntry Entry, string RawLine)> changedEntries,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            bufferSize: 1024 * 1024,
            leaveOpen: true);

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
        stream.Flush(flushToDisk: true);
    }

    private static async Task ValidateTemporaryFileAsync(
        string tempPath,
        int expectedEntries,
        CancellationToken cancellationToken)
    {
        var actualEntries = 0;

        await using var stream = new FileStream(
            tempPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024,
            leaveOpen: false);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            actualEntries++;

            try
            {
                using var json = JsonDocument.Parse(line);

                if (json.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"Проверка сохранения не пройдена: строка {actualEntries:N0} не является JSON-объектом.");
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Проверка сохранения не пройдена: повреждён JSON в строке {actualEntries:N0}. Исходный файл не заменён.",
                    ex);
            }
        }

        if (actualEntries != expectedEntries)
        {
            throw new InvalidDataException(
                $"Проверка сохранения не пройдена: ожидалось {expectedEntries:N0} строк, записано {actualEntries:N0}. " +
                "Исходный файл не заменён.");
        }
    }

    private static async Task ReplaceTargetWithRetryAsync(
        string tempPath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Replace(
                            tempPath,
                            targetPath,
                            destinationBackupFileName: null,
                            ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(
                            tempPath,
                            targetPath,
                            overwrite: true);
                    }
                }
                else
                {
                    File.Move(
                        tempPath,
                        targetPath);
                }

                return;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;

                if (attempt == maxAttempts)
                    break;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(150 * attempt),
                    cancellationToken);
            }
        }

        throw new IOException(
            $"Не удалось заменить «{Path.GetFileName(targetPath)}» после {maxAttempts} попыток. " +
            "Временный файл был успешно записан и проверен, но исходный файл занят другой программой " +
            "или недоступен для замены.",
            lastError);
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Остаточный temp не должен скрывать исходную ошибку сохранения.
        }
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
