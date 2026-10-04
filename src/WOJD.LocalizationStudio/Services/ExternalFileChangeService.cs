using System.IO;

namespace WOJD.LocalizationStudio.Services;

public readonly record struct FileStamp(
    long Length,
    DateTime LastWriteUtc,
    bool Exists);

public static class ExternalFileChangeService
{
    public static FileStamp Capture(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);

            if (!info.Exists)
                return new FileStamp(0, DateTime.MinValue, false);

            return new FileStamp(
                info.Length,
                info.LastWriteTimeUtc,
                true);
        }
        catch
        {
            return new FileStamp(0, DateTime.MinValue, false);
        }
    }

    public static bool HasChanged(
        string filePath,
        FileStamp baseline)
    {
        var current = Capture(filePath);

        if (current.Exists != baseline.Exists)
            return true;

        if (!current.Exists)
            return false;

        return current.Length != baseline.Length ||
               current.LastWriteUtc != baseline.LastWriteUtc;
    }
}
