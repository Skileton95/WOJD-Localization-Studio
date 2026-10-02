using System.IO;
using System.Text;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class LocresLocalizationAdapter : ILocalizationFileAdapter
{
    private static readonly byte[] Magic =
    [
        0x0E, 0x14, 0x74, 0x75,
        0x67, 0x4A, 0x03, 0xFC,
        0x4A, 0x15, 0x90, 0x9D,
        0xC3, 0x37, 0x7F, 0x1B
    ];

    public string Id => "locres";
    public string DisplayName => "Unreal Engine LOCRES";
    public IReadOnlyCollection<string> Extensions { get; } = [".locres"];

    public bool CanOpen(string path)
        => string.Equals(
            Path.GetExtension(path),
            ".locres",
            StringComparison.OrdinalIgnoreCase);

    public Task<LocalizationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => LoadCore(path, cancellationToken),
            cancellationToken);

    private LocalizationDocument LoadCore(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);

        var document =
            new LocalizationDocument
            {
                FilePath = path,
                AdapterId = Id,
                LoadedLastWriteTimeUtc = info.LastWriteTimeUtc,
                LoadedFileLength = info.Length
            };

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan);

        using var reader =
            new BinaryReader(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

        var version =
            ReadVersion(reader);

        string[]? stringTable = null;

        if (version >= LocresVersion.Compact)
        {
            var stringTableOffset =
                reader.ReadInt64();

            var returnPosition =
                reader.BaseStream.Position;

            reader.BaseStream.Position =
                stringTableOffset;

            var stringCount =
                reader.ReadInt32();

            if (stringCount < 0 ||
                stringCount > 50_000_000)
            {
                throw new InvalidDataException(
                    "Некорректная таблица строк LOCRES.");
            }

            stringTable =
                new string[stringCount];

            for (var i = 0; i < stringCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                stringTable[i] =
                    ReadUnrealString(reader);

                if (version >= LocresVersion.Optimized)
                    _ = reader.ReadInt32();
            }

            reader.BaseStream.Position =
                returnPosition;
        }

        if (version >= LocresVersion.Optimized)
            _ = reader.ReadInt32();

        var namespaceCount =
            reader.ReadInt32();

        if (namespaceCount < 0 ||
            namespaceCount > 10_000_000)
        {
            throw new InvalidDataException(
                "Некорректное количество Namespace в LOCRES.");
        }

        var index = 1;
        var metadata =
            new LocresDocumentMetadata(version);

        for (var namespaceIndex = 0;
             namespaceIndex < namespaceCount;
             namespaceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint namespaceHash = 0;

            if (version >= LocresVersion.Optimized)
                namespaceHash = reader.ReadUInt32();

            var ns =
                ReadUnrealString(reader);

            var keyCount =
                reader.ReadInt32();

            if (keyCount < 0 ||
                keyCount > 50_000_000)
            {
                throw new InvalidDataException(
                    $"Некорректное количество ключей в Namespace {ns}.");
            }

            for (var keyIndex = 0;
                 keyIndex < keyCount;
                 keyIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint keyHash = 0;

                if (version >= LocresVersion.Optimized)
                    keyHash = reader.ReadUInt32();

                var key =
                    ReadUnrealString(reader);

                var sourceHash =
                    reader.ReadUInt32();

                string value;

                if (version >= LocresVersion.Compact)
                {
                    var stringIndex =
                        reader.ReadInt32();

                    if (stringTable is null ||
                        stringIndex < 0 ||
                        stringIndex >= stringTable.Length)
                    {
                        throw new InvalidDataException(
                            $"Некорректный индекс строки LOCRES: {stringIndex}.");
                    }

                    value =
                        stringTable[stringIndex];
                }
                else
                {
                    value =
                        ReadUnrealString(reader);
                }

                var entryMetadata =
                    new LocresEntryMetadata(
                        namespaceHash,
                        keyHash,
                        sourceHash);

                var entry =
                    new LocalizationEntry
                    {
                        Index = index++,
                        Namespace = ns,
                        Key = key,
                        Original = value,
                        TranslationField = "locres",
                        AdapterMetadata = entryMetadata
                    };

                entry.InitializeSavedTranslation(value);
                document.Entries.Add(entry);
            }
        }

        document.AdapterMetadata =
            metadata;

        return document;
    }

    public async Task SaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document.AdapterMetadata is not LocresDocumentMetadata metadata)
        {
            throw new InvalidDataException(
                "Отсутствуют метаданные LOCRES.");
        }

        var tempPath =
            document.FilePath + ".tmp";

        await using var stream =
            new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                1024 * 1024,
                useAsync: false);

        using var writer =
            new BinaryWriter(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

        WriteDocument(
            writer,
            document,
            metadata.Version,
            cancellationToken);

        writer.Flush();
        await stream.FlushAsync(cancellationToken);
        stream.Close();

        File.Move(
            tempPath,
            document.FilePath,
            overwrite: true);

        foreach (var entry in document.Entries)
            entry.MarkSaved();

        var info =
            new FileInfo(document.FilePath);

        document.LoadedLastWriteTimeUtc =
            info.LastWriteTimeUtc;

        document.LoadedFileLength =
            info.Length;
    }

    private static void WriteDocument(
        BinaryWriter writer,
        LocalizationDocument document,
        LocresVersion version,
        CancellationToken cancellationToken)
    {
        var namespaces =
            document.Entries
                .GroupBy(x => x.Namespace)
                .ToList();

        if (version == LocresVersion.Legacy)
        {
            writer.Write(namespaces.Count);

            foreach (var ns in namespaces)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WriteUnrealString(
                    writer,
                    ns.Key,
                    forceUnicode: true);

                writer.Write(ns.Count());

                foreach (var entry in ns)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    WriteUnrealString(writer, entry.Key);

                    var em =
                        entry.AdapterMetadata
                        as LocresEntryMetadata;

                    writer.Write(
                        em?.SourceHash ?? 0U);

                    WriteUnrealString(
                        writer,
                        entry.Translation);
                }
            }

            return;
        }

        writer.Write(Magic);
        writer.Write((byte)version);

        var tableOffsetPosition =
            writer.BaseStream.Position;

        writer.Write(0L);

        if (version >= LocresVersion.Optimized)
            writer.Write(document.Entries.Count);

        writer.Write(namespaces.Count);

        var table =
            new List<StringTableEntry>();

        var indexMap =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        foreach (var ns in namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstMetadata =
                ns.FirstOrDefault()?.AdapterMetadata
                as LocresEntryMetadata;

            if (version >= LocresVersion.Optimized)
            {
                writer.Write(
                    firstMetadata?.NamespaceHash
                    ?? 0U);
            }

            WriteUnrealString(
                writer,
                ns.Key);

            writer.Write(ns.Count());

            foreach (var entry in ns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var em =
                    entry.AdapterMetadata
                    as LocresEntryMetadata;

                if (version >= LocresVersion.Optimized)
                {
                    writer.Write(
                        em?.KeyHash
                        ?? 0U);
                }

                WriteUnrealString(
                    writer,
                    entry.Key);

                writer.Write(
                    em?.SourceHash
                    ?? 0U);

                if (!indexMap.TryGetValue(
                        entry.Translation,
                        out var stringIndex))
                {
                    stringIndex =
                        table.Count;

                    indexMap[
                        entry.Translation] =
                        stringIndex;

                    table.Add(
                        new StringTableEntry(
                            entry.Translation,
                            1));
                }
                else
                {
                    var item =
                        table[stringIndex];

                    table[stringIndex] =
                        item with
                        {
                            RefCount =
                                item.RefCount + 1
                        };
                }

                writer.Write(stringIndex);
            }
        }

        var tableOffset =
            writer.BaseStream.Position;

        writer.Write(table.Count);

        foreach (var item in table)
        {
            WriteUnrealString(
                writer,
                item.Text);

            if (version >= LocresVersion.Optimized)
                writer.Write(item.RefCount);
        }

        var end =
            writer.BaseStream.Position;

        writer.BaseStream.Position =
            tableOffsetPosition;

        writer.Write(tableOffset);

        writer.BaseStream.Position =
            end;
    }

    private static LocresVersion ReadVersion(
        BinaryReader reader)
    {
        var start =
            reader.BaseStream.Position;

        var magic =
            reader.ReadBytes(Magic.Length);

        if (magic.Length == Magic.Length &&
            magic.SequenceEqual(Magic))
        {
            var raw =
                reader.ReadByte();

            if (raw > (byte)LocresVersion.OptimizedCityHash64Utf16)
            {
                throw new InvalidDataException(
                    $"Неизвестная версия LOCRES: {raw}.");
            }

            return (LocresVersion)raw;
        }

        reader.BaseStream.Position =
            start;

        return LocresVersion.Legacy;
    }

    private static string ReadUnrealString(
        BinaryReader reader)
    {
        var length =
            reader.ReadInt32();

        if (length == 0)
            return string.Empty;

        if (length < 0)
        {
            var charCount =
                -length;

            if (charCount <= 0 ||
                charCount > 100_000_000)
            {
                throw new InvalidDataException(
                    "Некорректная UTF-16 строка LOCRES.");
            }

            var bytes =
                reader.ReadBytes(
                    checked(charCount * 2));

            if (bytes.Length != charCount * 2)
            {
                throw new EndOfStreamException(
                    "Неожиданный конец UTF-16 строки LOCRES.");
            }

            var text =
                Encoding.Unicode.GetString(bytes);

            return text.TrimEnd('\0');
        }

        if (length > 100_000_000)
        {
            throw new InvalidDataException(
                "Некорректная ANSI строка LOCRES.");
        }

        var data =
            reader.ReadBytes(length);

        if (data.Length != length)
        {
            throw new EndOfStreamException(
                "Неожиданный конец строки LOCRES.");
        }

        if (data.Length > 0 &&
            data[^1] == 0)
        {
            data =
                data[..^1];
        }

        return Encoding.UTF8.GetString(data);
    }

    private static void WriteUnrealString(
        BinaryWriter writer,
        string value,
        bool forceUnicode = false)
    {
        value ??= string.Empty;

        var useUnicode =
            forceUnicode ||
            value.Any(ch => ch > 0x7F);

        if (useUnicode)
        {
            writer.Write(
                -(value.Length + 1));

            writer.Write(
                Encoding.Unicode.GetBytes(
                    value + "\0"));

            return;
        }

        var bytes =
            Encoding.UTF8.GetBytes(
                value + "\0");

        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private sealed record StringTableEntry(
        string Text,
        int RefCount);
}

public enum LocresVersion : byte
{
    Legacy = 0,
    Compact = 1,
    Optimized = 2,
    OptimizedCityHash64Utf16 = 3
}

public sealed record LocresDocumentMetadata(
    LocresVersion Version);

public sealed record LocresEntryMetadata(
    uint NamespaceHash,
    uint KeyHash,
    uint SourceHash);
