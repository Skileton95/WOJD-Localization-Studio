using System.IO;

namespace WOJD.LocalizationStudio.Services;

public sealed record BackupInfo(
    string Path,
    DateTime CreatedLocal,
    long Size);

public static class BackupService
{
    public static string? CreateBackup(
        string filePath,
        int? keepLimit = null)
    {
        if (!File.Exists(filePath))
            return null;

        var dir =
            Path.Combine(
                Path.GetDirectoryName(filePath)!,
                ".localization-backups");

        Directory.CreateDirectory(dir);

        var stamp =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss_fff");

        var name =
            $"{Path.GetFileName(filePath)}.{stamp}.{Guid.NewGuid():N}.bak";

        var backupPath =
            Path.Combine(
                dir,
                name);

        File.Copy(
            filePath,
            backupPath,
            overwrite: false);

        Prune(
            filePath,
            keepLimit
            ?? AppSettingsService.Current.BackupLimit);

        return backupPath;
    }

    public static IReadOnlyList<BackupInfo> ListBackups(
        string filePath)
    {
        var dir =
            Path.Combine(
                Path.GetDirectoryName(filePath)!,
                ".localization-backups");

        if (!Directory.Exists(dir))
            return [];

        var prefix =
            Path.GetFileName(filePath) + ".";

        return Directory
            .EnumerateFiles(dir)
            .Where(x =>
                Path.GetFileName(x)
                    .StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var info = new FileInfo(x);

                return new BackupInfo(
                    x,
                    info.CreationTime,
                    info.Length);
            })
            .OrderByDescending(x => x.CreatedLocal)
            .ToList();
    }

    public static void RestoreBackup(
        string backupPath,
        string targetPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException(
                "Резервная копия не найдена.",
                backupPath);
        }

        File.Copy(
            backupPath,
            targetPath,
            overwrite: true);
    }

    public static void Prune(
        string filePath,
        int keepLimit)
    {
        if (keepLimit <= 0)
            return;

        var backups =
            ListBackups(filePath);

        foreach (var backup in
                 backups.Skip(keepLimit))
        {
            try
            {
                File.Delete(backup.Path);
            }
            catch
            {
            }
        }
    }
}
