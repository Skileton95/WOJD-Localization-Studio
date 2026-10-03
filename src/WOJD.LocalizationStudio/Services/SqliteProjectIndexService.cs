using System.IO;
using Microsoft.Data.Sqlite;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record IndexFileSummary(string FilePath, long Rows, string Sha256);
public static class SqliteProjectIndexService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string DatabasePath => Path.Combine(WorkspaceStateService.StorageDirectory, "project-index-v1.sqlite");
    private static SqliteConnection Open()
    {
        Directory.CreateDirectory(WorkspaceStateService.StorageDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        try
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS Files(Path TEXT PRIMARY KEY, DisplayPath TEXT NOT NULL, Hash TEXT NOT NULL, Rows INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS Entries(Path TEXT NOT NULL, RowIndex INTEGER NOT NULL, Namespace TEXT NOT NULL, EntryKey TEXT NOT NULL, Source TEXT NOT NULL, Translation TEXT NOT NULL, PRIMARY KEY(Path,RowIndex));
                CREATE INDEX IF NOT EXISTS IdentityIndex ON Entries(Namespace,EntryKey);
                CREATE INDEX IF NOT EXISTS SourceIndex ON Entries(Source);
                CREATE TABLE IF NOT EXISTS Metadata(Path TEXT PRIMARY KEY, Hash TEXT NOT NULL, Json TEXT NOT NULL);
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery(); return connection;
        } catch { connection.Dispose(); throw; }
    }
    private static void PreserveDatabase()
    {
        var suffix = ".preserved-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
        foreach (var extension in new[] { "", "-wal", "-shm" }) if (File.Exists(DatabasePath + extension)) File.Move(DatabasePath + extension, DatabasePath + suffix + extension);
    }
    public static async Task ResetAsync()
    {
        await Gate.WaitAsync();
        try { await Task.Run(() => { PreserveDatabase(); using var connection = Open(); }); }
        finally { Gate.Release(); }
    }
    public static async Task EnsureAsync(string file, CancellationToken cancellation, IProgress<FileOperationProgress>? progress)
    {
        await Gate.WaitAsync(cancellation);
        try
        {
            var hash = await Task.Run(() => FileSafetyService.Hash(file), cancellation); var identity = Path.GetFullPath(file).ToUpperInvariant();
            bool Current()
            {
                using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT Hash FROM Files WHERE Path=$path"; command.Parameters.AddWithValue("$path", identity);
                return command.ExecuteScalar() as string == hash;
            }
            bool current;
            try { current = await Task.Run(Current, cancellation); }
            catch (SqliteException e) when (e.SqliteErrorCode is 11 or 26) { PreserveDatabase(); current = false; IssueLogService.Record("Повреждённый индекс сохранён; выполняется пересоздание."); }
            if (current) return;
            var cache = await ProjectSearchIndexService.EnsureAsync(file, cancellation, progress);
            await Task.Run(() =>
            {
                using var connection = Open(); using var transaction = connection.BeginTransaction();
                using var clear = connection.CreateCommand(); clear.Transaction = transaction; clear.CommandText = "DELETE FROM Entries WHERE Path=$path"; clear.Parameters.AddWithValue("$path", identity); clear.ExecuteNonQuery();
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO Entries VALUES($path,$index,$namespace,$key,$source,$translation)";
                foreach (var name in new[] { "$path", "$index", "$namespace", "$key", "$source", "$translation" }) insert.Parameters.Add(new SqliteParameter(name, name == "$index" ? SqliteType.Integer : SqliteType.Text));
                insert.Parameters["$path"].Value = identity; long count = 0;
                foreach (var row in ProjectSearchIndexService.ReadRows(cache, cancellation))
                {
                    cancellation.ThrowIfCancellationRequested();
                    insert.Parameters["$index"].Value = row.Index; insert.Parameters["$namespace"].Value = row.Namespace; insert.Parameters["$key"].Value = row.Key;
                    insert.Parameters["$source"].Value = row.Original; insert.Parameters["$translation"].Value = row.Translation; insert.ExecuteNonQuery(); count++;
                    if ((count & 4095) == 0) progress?.Report(new(0, (int)Math.Min(count, int.MaxValue), "SQLite: " + count));
                }
                if (FileSafetyService.Hash(file) != hash) throw new IOException("Файл изменился во время индексации.");
                using var update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = "INSERT OR REPLACE INTO Files VALUES($path,$display,$hash,$rows)";
                update.Parameters.AddWithValue("$path", identity); update.Parameters.AddWithValue("$display", Path.GetFullPath(file)); update.Parameters.AddWithValue("$hash", hash); update.Parameters.AddWithValue("$rows", count); update.ExecuteNonQuery(); transaction.Commit();
            }, cancellation);
        } finally { Gate.Release(); }
    }
    public static IEnumerable<EntryLocation> Read(string file, CancellationToken cancellation)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT RowIndex,Namespace,EntryKey,Source,Translation FROM Entries WHERE Path=$path ORDER BY RowIndex";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(file).ToUpperInvariant());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            var entry = new LocalizationEntry { Index = reader.GetInt32(0), Namespace = reader.GetString(1), Key = reader.GetString(2), Original = reader.GetString(3) };
            entry.InitializeSavedTranslation(reader.GetString(4)); yield return new(file, entry);
        }
    }
    public static List<IndexFileSummary> Statistics()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT DisplayPath,Rows,Hash FROM Files ORDER BY DisplayPath";
        using var reader = command.ExecuteReader(); var result = new List<IndexFileSummary>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2))); return result;
    }
    public static async Task ProjectMetadataAsync(IEnumerable<string> paths)
    {
        await Gate.WaitAsync();
        try { await Task.Run(() =>
        {
            using var connection = Open(); using var transaction = connection.BeginTransaction();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT OR REPLACE INTO Metadata VALUES($path,$hash,$json)";
                command.Parameters.AddWithValue("$path", Path.GetFullPath(path)); command.Parameters.AddWithValue("$hash", FileSafetyService.Hash(path)); command.Parameters.AddWithValue("$json", File.ReadAllText(path)); command.ExecuteNonQuery();
            } transaction.Commit();
        }); } finally { Gate.Release(); }
    }
}
