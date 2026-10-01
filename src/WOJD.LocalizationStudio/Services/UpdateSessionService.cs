using System.IO;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class UpdateSessionService
{
    private static readonly string SessionDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WOJD Localization Studio");

    private static readonly string SessionPath = Path.Combine(
        SessionDirectory,
        "update-session.json");

    public async Task SaveAsync(
        IEnumerable<string> filePaths,
        string? activeFilePath,
        CancellationToken cancellationToken = default)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            Clear();
            return;
        }

        Directory.CreateDirectory(SessionDirectory);

        var session = new UpdateSession
        {
            FilePaths = paths,
            ActiveFilePath = activeFilePath
        };

        await using var stream = File.Create(SessionPath);
        await JsonSerializer.SerializeAsync(
            stream,
            session,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }

    public async Task<UpdateSession?> LoadAndConsumeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath))
            return null;

        try
        {
            await using var stream = File.OpenRead(SessionPath);
            return await JsonSerializer.DeserializeAsync<UpdateSession>(
                stream,
                cancellationToken: cancellationToken);
        }
        finally
        {
            Clear();
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(SessionPath))
                File.Delete(SessionPath);
        }
        catch
        {
            // Сбой очистки временной сессии не должен мешать работе редактора.
        }
    }
}

public sealed class UpdateSession
{
    public string[] FilePaths { get; set; } = Array.Empty<string>();
    public string? ActiveFilePath { get; set; }
}
