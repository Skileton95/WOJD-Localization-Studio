using System.IO;

namespace WOJD.LocalizationStudio.Services;

public static class BackupService
{
    private const int MaxBackupsPerFile = 10;

    public static string? CreateBackup(
        string filePath,
        string? reason = null)
    {
        if (!File.Exists(filePath))
            return null;

        var fullPath = Path.GetFullPath(filePath);
        var sourceDirectory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return null;

        var backupDirectory =
            Path.Combine(sourceDirectory, ".localization-backups");

        Directory.CreateDirectory(backupDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var reasonSuffix =
            string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : "." + SanitizeFilePart(reason);

        var fileName = Path.GetFileName(fullPath);
        var backupPath =
            Path.Combine(
                backupDirectory,
                $"{fileName}.{stamp}{reasonSuffix}.bak");

        var collisionIndex = 1;
        while (File.Exists(backupPath))
        {
            backupPath =
                Path.Combine(
                    backupDirectory,
                    $"{fileName}.{stamp}{reasonSuffix}.{collisionIndex++}.bak");
        }

        File.Copy(
            fullPath,
            backupPath,
            overwrite: false);

        TrimOldBackups(
            backupDirectory,
            fileName);

        return backupPath;
    }

    private static void TrimOldBackups(
        string backupDirectory,
        string sourceFileName)
    {
        IEnumerable<string> backups;

        try
        {
            backups =
                Directory
                    .EnumerateFiles(
                        backupDirectory,
                        $"{sourceFileName}.*.bak",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetCreationTimeUtc)
                    .ThenByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .Skip(MaxBackupsPerFile)
                    .ToArray();
        }
        catch
        {
            return;
        }

        foreach (var backup in backups)
        {
            try
            {
                File.Delete(backup);
            }
            catch
            {
                // Ошибка очистки старого бэкапа не должна мешать работе редактора.
            }
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();

        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result)
            ? "backup"
            : result;
    }
}
