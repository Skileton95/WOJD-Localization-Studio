using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace WOJD.LocalizationStudio.Services;

public static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Skileton95/WOJD-Localization-Studio/releases/latest";
    private const string PackageAssetName = "WOJD-Localization-Studio-win-x64.zip";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim CheckLock = new(1, 1);
    private static DispatcherTimer? _timer;
    private static string? _ignoredVersion;

    public static async Task StartAsync(Window owner)
    {
        UpdateScheduler.TryRegister();
        await CheckAndPromptAsync(owner, showErrors: false);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += async (_, _) => await CheckAndPromptAsync(owner, showErrors: false);
        _timer.Start();
    }

    public static async Task CheckAndPromptAsync(Window owner, bool showErrors)
    {
        if (!await CheckLock.WaitAsync(0)) return;
        try
        {
            var release = await GetLatestReleaseAsync();
            if (release is null) return;

            var current = GetCurrentVersion();
            if (release.Version <= current || release.VersionText == _ignoredVersion) return;

            var result = MessageBox.Show(
                owner,
                $"Доступна новая версия WOJD Localization Studio {release.VersionText}.\n\nУстановить обновление сейчас? Программа будет перезапущена автоматически.",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                _ignoredVersion = release.VersionText;
                return;
            }

            if (!UpdaterLauncher.TryLaunch(release.DownloadUrl, release.VersionText, restart: true))
            {
                MessageBox.Show(owner, "Не найден компонент обновления. Установите текущую опубликованную сборку из GitHub Releases один раз, после чего обновления будут работать автоматически.", "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (showErrors)
                MessageBox.Show(owner, ex.Message, "Проверка обновлений", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CheckLock.Release();
        }
    }

    private static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using var response = await Http.GetAsync(LatestReleaseUrl);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var version)) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), PackageAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrWhiteSpace(url))
                return new ReleaseInfo(version, versionText, url);
        }

        return null;
    }

    private static Version GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record ReleaseInfo(Version Version, string VersionText, string DownloadUrl);
}

internal static class UpdaterLauncher
{
    public static bool TryLaunch(string packageUrl, string version, bool restart)
    {
        var updater = Path.Combine(AppContext.BaseDirectory, "Updater", "WOJD-Localization-Studio.Updater.exe");
        if (!File.Exists(updater)) return false;

        var appExe = Path.Combine(AppContext.BaseDirectory, "WOJD-Localization-Studio.exe");
        var info = new ProcessStartInfo(updater) { UseShellExecute = false };
        info.ArgumentList.Add("--apply");
        info.ArgumentList.Add("--install-dir");
        info.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        info.ArgumentList.Add("--package-url");
        info.ArgumentList.Add(packageUrl);
        info.ArgumentList.Add("--version");
        info.ArgumentList.Add(version);
        info.ArgumentList.Add("--wait-pid");
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        if (restart)
        {
            info.ArgumentList.Add("--restart");
            info.ArgumentList.Add(appExe);
        }

        Process.Start(info);
        return true;
    }
}

internal static class UpdateScheduler
{
    private const string TaskName = "WOJD Localization Studio Updater";

    public static void TryRegister()
    {
        try
        {
            var updater = Path.Combine(AppContext.BaseDirectory, "Updater", "WOJD-Localization-Studio.Updater.exe");
            if (!File.Exists(updater)) return;

            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var taskCommand = $"\"{updater}\" --scheduled --install-dir \"{installDir}\"";
            var info = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            info.ArgumentList.Add("/Create");
            info.ArgumentList.Add("/TN");
            info.ArgumentList.Add(TaskName);
            info.ArgumentList.Add("/TR");
            info.ArgumentList.Add(taskCommand);
            info.ArgumentList.Add("/SC");
            info.ArgumentList.Add("MINUTE");
            info.ArgumentList.Add("/MO");
            info.ArgumentList.Add("15");
            info.ArgumentList.Add("/F");

            using var process = Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Автообновление при запущенной программе продолжает работать даже если Планировщик недоступен.
        }
    }
}
