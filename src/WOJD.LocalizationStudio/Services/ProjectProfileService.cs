using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed record ProjectProfile(
    string Name,
    string RootPath,
    string NativeLanguage = "zh-Hans",
    string TargetLanguage = "ru-RU",
    string? SourceLocresPath = null,
    string[]? IncludePatterns = null);

public static class ProjectProfileService
{
    public const string FileName = ".wojdproject.json";

    public static ProjectProfile CreateDefault(string rootPath)
        => new(
            Path.GetFileName(
                rootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
            Path.GetFullPath(rootPath),
            "zh-Hans",
            "ru-RU",
            null,
            ["*.ndjson", "*.jsonl", "*.locres"]);

    public static ProjectProfile? Load(string rootPath)
    {
        try
        {
            var path =
                Path.Combine(
                    Path.GetFullPath(rootPath),
                    FileName);

            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<ProjectProfile>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ProjectProfile profile)
    {
        Directory.CreateDirectory(profile.RootPath);

        var path =
            Path.Combine(
                profile.RootPath,
                FileName);

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                profile,
                JsonOptions));
    }

    public static string? FindProjectRoot(string fileOrFolder)
    {
        var directory =
            Directory.Exists(fileOrFolder)
                ? new DirectoryInfo(Path.GetFullPath(fileOrFolder))
                : new FileInfo(Path.GetFullPath(fileOrFolder)).Directory;

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        FileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
}
