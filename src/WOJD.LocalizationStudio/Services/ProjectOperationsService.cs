using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record GitCommandResult(
    bool Success,
    int ExitCode,
    string Output,
    string Error);

public static class GitService
{
    public static async Task<GitCommandResult> RunAsync(
        string rootPath,
        params string[] args)
    {
        try
        {
            var info =
                new ProcessStartInfo("git")
                {
                    WorkingDirectory = rootPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

            foreach (var arg in args)
                info.ArgumentList.Add(arg);

            using var process =
                Process.Start(info);

            if (process is null)
            {
                return new GitCommandResult(
                    false,
                    -1,
                    string.Empty,
                    "Не удалось запустить git.");
            }

            var outputTask =
                process.StandardOutput.ReadToEndAsync();

            var errorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new GitCommandResult(
                process.ExitCode == 0,
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (Exception ex)
        {
            return new GitCommandResult(
                false,
                -1,
                string.Empty,
                ex.Message);
        }
    }

    public static Task<GitCommandResult> StatusAsync(
        string rootPath)
        => RunAsync(
            rootPath,
            "status",
            "--short",
            "--branch");

    public static Task<GitCommandResult> CommitAllAsync(
        string rootPath,
        string message)
        => CommitInternalAsync(
            rootPath,
            message);

    private static async Task<GitCommandResult> CommitInternalAsync(
        string rootPath,
        string message)
    {
        var add =
            await RunAsync(
                rootPath,
                "add",
                "-A");

        if (!add.Success)
            return add;

        return await RunAsync(
            rootPath,
            "commit",
            "-m",
            message);
    }

    public static Task<GitCommandResult> FileHistoryAsync(
        string rootPath,
        string filePath)
        => RunAsync(
            rootPath,
            "log",
            "--oneline",
            "--decorate",
            "-n",
            "50",
            "--",
            Path.GetRelativePath(
                rootPath,
                filePath));
}

public static class ReleasePackageService
{
    public static async Task<string> CreateAsync(
        string rootPath,
        string outputZip,
        string version,
        IEnumerable<string> files,
        string changelog)
    {
        var temp =
            Path.Combine(
                Path.GetTempPath(),
                "WOJD-Localization-Studio",
                "release-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            foreach (var file in files.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file))
                    continue;

                var relative =
                    Path.GetRelativePath(
                        rootPath,
                        file);

                if (relative.StartsWith(".."))
                    continue;

                var target =
                    Path.Combine(
                        temp,
                        relative);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(target)!);

                File.Copy(
                    file,
                    target,
                    true);
            }

            await File.WriteAllTextAsync(
                Path.Combine(
                    temp,
                    "CHANGELOG.txt"),
                $"WOJD Localization Studio package {version}{Environment.NewLine}{Environment.NewLine}{changelog}",
                Encoding.UTF8);

            var manifest =
                new
                {
                    Version = version,
                    CreatedUtc = DateTime.UtcNow,
                    Files = files
                        .Where(File.Exists)
                        .Select(x =>
                            Path.GetRelativePath(
                                rootPath,
                                x))
                        .ToArray()
                };

            await File.WriteAllTextAsync(
                Path.Combine(
                    temp,
                    "manifest.json"),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                Encoding.UTF8);

            if (File.Exists(outputZip))
                File.Delete(outputZip);

            ZipFile.CreateFromDirectory(
                temp,
                outputZip,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            return outputZip;
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temp,
                    true);
            }
            catch
            {
            }
        }
    }
}

public sealed record SyncConflict(
    string Namespace,
    string Key,
    string Kind,
    string[] Originals,
    string[] Translations,
    int Occurrences);

public sealed record SyncSummary(
    int Added,
    int Updated,
    int SkippedTranslated,
    int Conflicts);

public static class ProjectSyncService
{
    public static List<SyncConflict> FindConflicts(
        IEnumerable<LocalizationDocument> documents)
    {
        var result =
            new List<SyncConflict>();

        foreach (var group in
                 documents
                     .SelectMany(x => x.Entries)
                     .GroupBy(
                         x => $"{x.Namespace}\u001F{x.Key}",
                         StringComparer.Ordinal))
        {
            var originals =
                group
                    .Select(x => x.Original)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

            var translations =
                group
                    .Select(x => x.Translation)
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

            if (originals.Length <= 1 &&
                translations.Length <= 1)
            {
                continue;
            }

            result.Add(
                new SyncConflict(
                    group.First().Namespace,
                    group.First().Key,
                    originals.Length > 1
                        ? "Разный source для Namespace+Key"
                        : "Разные переводы для Namespace+Key",
                    originals,
                    translations,
                    group.Count()));
        }

        return result;
    }

    public static SyncSummary SyncInto(
        LocalizationDocument target,
        IEnumerable<LocalizationDocument> sources,
        bool protectExistingTranslations)
    {
        var sourceMap =
            sources
                .SelectMany(x => x.Entries)
                .GroupBy(
                    x => $"{x.Namespace}\u001F{x.Key}",
                    StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToList(),
                    StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var conflicts = 0;

        foreach (var targetEntry in target.Entries)
        {
            var id =
                $"{targetEntry.Namespace}\u001F{targetEntry.Key}";

            if (!sourceMap.TryGetValue(
                    id,
                    out var candidates))
            {
                continue;
            }

            var matchingSource =
                candidates
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Original,
                                targetEntry.Original,
                                StringComparison.Ordinal));

            if (matchingSource is null)
            {
                conflicts++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    matchingSource.Translation))
            {
                continue;
            }

            if (protectExistingTranslations &&
                !string.IsNullOrWhiteSpace(
                    targetEntry.Translation))
            {
                skipped++;
                continue;
            }

            if (!string.Equals(
                    targetEntry.Translation,
                    matchingSource.Translation,
                    StringComparison.Ordinal))
            {
                targetEntry.Translation =
                    matchingSource.Translation;

                updated++;
            }
        }

        return new SyncSummary(
            added,
            updated,
            skipped,
            conflicts);
    }

    public static int TransferSafeTranslations(
        LocalizationDocument oldDocument,
        LocalizationDocument newDocument)
    {
        var oldMap =
            oldDocument.Entries
                .GroupBy(
                    x => $"{x.Namespace}\u001F{x.Key}",
                    StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.First(),
                    StringComparer.Ordinal);

        var transferred = 0;

        foreach (var current in newDocument.Entries)
        {
            var id =
                $"{current.Namespace}\u001F{current.Key}";

            if (!oldMap.TryGetValue(
                    id,
                    out var previous))
            {
                continue;
            }

            if (!string.Equals(
                    current.Original,
                    previous.Original,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    previous.Translation))
            {
                continue;
            }

            current.Translation =
                previous.Translation;

            transferred++;
        }

        return transferred;
    }
}

public static class CollaborationService
{
    public static void ExportAssignments(
        string path,
        IEnumerable<AssignmentRecord> assignments)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                assignments,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    public static List<AssignmentRecord> ImportAssignments(
        string path)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AssignmentRecord>>(
                       File.ReadAllText(path))
                   ?? [];
        }
        catch
        {
            return [];
        }
    }
}
