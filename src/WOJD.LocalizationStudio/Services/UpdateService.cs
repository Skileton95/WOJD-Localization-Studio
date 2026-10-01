using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class UpdateService
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/Skileton95/WOJD-Localization-Studio/main/update.json";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var json = await HttpClient.GetStringAsync(ManifestUrl, cancellationToken);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            return null;

        if (!Version.TryParse(CurrentVersion, out var currentVersion) ||
            !Version.TryParse(manifest.Version, out var remoteVersion))
            return null;

        if (remoteVersion <= currentVersion)
            return null;

        return new UpdateInfo(manifest.Version, manifest.Notes ?? string.Empty);
    }

    public bool TryLaunchUpdater(out string? error)
    {
        error = null;
        var updater = FindUpdater();

        if (updater is null)
        {
            error = "Не найден update.bat. Запустите программу из клонированной папки репозитория.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                Arguments = "--from-app",
                WorkingDirectory = Path.GetDirectoryName(updater)!,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? FindUpdater()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "update.bat");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio");
        return client;
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}

public sealed record UpdateInfo(string Version, string Notes);
