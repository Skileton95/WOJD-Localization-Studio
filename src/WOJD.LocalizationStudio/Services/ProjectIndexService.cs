using System.IO;
using Microsoft.Data.Sqlite;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record IndexedSearchResult(
    string FilePath,
    int RowIndex,
    string Namespace,
    string Key,
    string Original,
    string Translation,
    string Status,
    string Qa);

public static class ProjectIndexService
{
    public static async Task<string> RebuildAsync(
        string rootPath,
        IEnumerable<LocalizationDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var folder =
            Path.Combine(
                rootPath,
                ".wojd-studio");

        Directory.CreateDirectory(folder);

        var databasePath =
            Path.Combine(
                folder,
                "index.db");

        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath}");

        await connection.OpenAsync(cancellationToken);

        var create =
            connection.CreateCommand();

        create.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS entries (
                file_path TEXT NOT NULL,
                row_index INTEGER NOT NULL,
                namespace TEXT NOT NULL,
                key_text TEXT NOT NULL,
                original TEXT NOT NULL,
                translation TEXT NOT NULL,
                status TEXT NOT NULL,
                qa TEXT NOT NULL,
                PRIMARY KEY(file_path, row_index)
            );
            CREATE INDEX IF NOT EXISTS idx_entries_ns_key
                ON entries(namespace, key_text);
            CREATE INDEX IF NOT EXISTS idx_entries_original
                ON entries(original);
            CREATE INDEX IF NOT EXISTS idx_entries_translation
                ON entries(translation);
            DELETE FROM entries;
            """;

        await create.ExecuteNonQueryAsync(
            cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var insert =
                    connection.CreateCommand();

                insert.Transaction =
                    transaction;

                insert.CommandText =
                    """
                    INSERT INTO entries
                    (file_path, row_index, namespace, key_text, original, translation, status, qa)
                    VALUES
                    ($file, $row, $ns, $key, $original, $translation, $status, $qa);
                    """;

                insert.Parameters.AddWithValue(
                    "$file",
                    document.FilePath);

                insert.Parameters.AddWithValue(
                    "$row",
                    entry.Index);

                insert.Parameters.AddWithValue(
                    "$ns",
                    entry.Namespace);

                insert.Parameters.AddWithValue(
                    "$key",
                    entry.Key);

                insert.Parameters.AddWithValue(
                    "$original",
                    entry.Original);

                insert.Parameters.AddWithValue(
                    "$translation",
                    entry.Translation);

                insert.Parameters.AddWithValue(
                    "$status",
                    entry.StatusText);

                insert.Parameters.AddWithValue(
                    "$qa",
                    entry.ValidationSummary);

                await insert.ExecuteNonQueryAsync(
                    cancellationToken);
            }
        }

        await transaction.CommitAsync(
            cancellationToken);

        return databasePath;
    }

    public static async Task<List<IndexedSearchResult>> SearchAsync(
        string databasePath,
        string query,
        int limit = 5000,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<IndexedSearchResult>();

        if (!File.Exists(databasePath) ||
            string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        await using var connection =
            new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly");

        await connection.OpenAsync(
            cancellationToken);

        var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT file_path, row_index, namespace, key_text,
                   original, translation, status, qa
            FROM entries
            WHERE namespace LIKE $q COLLATE NOCASE
               OR key_text LIKE $q COLLATE NOCASE
               OR original LIKE $q COLLATE NOCASE
               OR translation LIKE $q COLLATE NOCASE
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue(
            "$q",
            $"%{query}%");

        command.Parameters.AddWithValue(
            "$limit",
            limit);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new IndexedSearchResult(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7)));
        }

        return result;
    }
}
