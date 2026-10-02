using System.IO;
namespace WOJD.LocalizationStudio.Services;

public static class BackupService
{
    public static void CreateBackup(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var dir = Path.Combine(Path.GetDirectoryName(filePath)!, ".localization-backups");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var name = $"{Path.GetFileName(filePath)}.{stamp}.bak";
        File.Copy(filePath, Path.Combine(dir, name), true);
    }
}
