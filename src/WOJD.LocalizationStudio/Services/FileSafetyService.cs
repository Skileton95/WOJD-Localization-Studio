using System.IO;
using System.Security.Cryptography;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class FileSafetyService
{
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static void CheckUnchanged(LocalizationDocument document)
    {
        if (document.DiskHash is not null && (!File.Exists(document.FilePath) || Hash(document.FilePath) != document.DiskHash))
            throw new IOException("Файл изменён другой программой или удалён. Сохранение отменено. Перезагрузите или сравните версии.");
    }
}