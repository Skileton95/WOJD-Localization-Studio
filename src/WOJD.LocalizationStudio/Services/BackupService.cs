using System.IO;
namespace WOJD.LocalizationStudio.Services;

public sealed record BackupVersion(string Path, DateTime CreatedUtc, long Bytes);
public static class BackupService
{
    public static string NewBackupPath(string filePath)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath)!, ".localization-backups");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileName(filePath)}.{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}.{Guid.NewGuid():N}.bak");
    }
    public static void CreateBackup(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var path = NewBackupPath(filePath);
        File.Copy(filePath, path, false); File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }
    public static IReadOnlyList<BackupVersion> List(string filePath)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath)!, ".localization-backups");
        if (!Directory.Exists(dir)) return [];
        var prefix = System.IO.Path.GetFileName(filePath) + ".";
        return Directory.EnumerateFiles(dir).Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .Select(p => new FileInfo(p)).Select(f => new BackupVersion(f.FullName, f.LastWriteTimeUtc, f.Length)).OrderByDescending(x => x.CreatedUtc).ToList();
    }
    public static void Prune(string filePath, int keep = 50)
    {
        if (keep < 1) return;
        foreach (var old in List(filePath).Skip(keep)) File.Delete(old.Path);
    }
}