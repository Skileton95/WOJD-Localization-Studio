using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio.Services;

public sealed record UpdateProgressState(
    bool IsVisible,
    double Progress,
    bool IsIndeterminate,
    string Status);

public static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Skileton95/WOJD-Localization-Studio/releases/latest";
    private const string PackageAssetName = "WOJD-Localization-Studio-win-x64.zip";

    private static readonly HttpClient ApiHttp = CreateApiHttpClient();
    private static readonly HttpClient DownloadHttp = CreateDownloadHttpClient();
    private static readonly SemaphoreSlim CheckLock = new(1, 1);
    private static DispatcherTimer? _timer;
    private static string? _ignoredVersion;

    public static bool IsApplyingUpdate { get; private set; }

    public static async Task StartAsync(
        Window owner,
        Action<UpdateProgressState> reportProgress,
        Func<bool> confirmDiscardUnsaved)
    {
        UpdateScheduler.TryRegister();

        await CheckAndPromptAsync(
            owner,
            reportProgress,
            confirmDiscardUnsaved,
            showErrors: false);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) =>
            await CheckAndPromptAsync(
                owner,
                reportProgress,
                confirmDiscardUnsaved,
                showErrors: false);

        _timer.Start();
    }

    private static async Task CheckAndPromptAsync(
        Window owner,
        Action<UpdateProgressState> reportProgress,
        Func<bool> confirmDiscardUnsaved,
        bool showErrors)
    {
        if (IsApplyingUpdate || !await CheckLock.WaitAsync(0))
            return;

        var progressStarted = false;

        try
        {
            var release = await GetLatestReleaseAsync();
            if (release is null)
                return;

            var current = GetCurrentVersion();
            if (release.Version <= current || release.VersionText == _ignoredVersion)
                return;

            var dialog = new UpdateAvailableDialog(release.VersionText)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true)
            {
                _ignoredVersion = release.VersionText;
                return;
            }

            if (!confirmDiscardUnsaved())
                return;

            IsApplyingUpdate = true;
            progressStarted = true;

            reportProgress(new UpdateProgressState(
                true,
                0,
                true,
                $"Подготовка обновления {release.VersionText}..."));

            var packagePath = await DownloadPackageAsync(release, reportProgress);

            reportProgress(new UpdateProgressState(
                true,
                100,
                false,
                "Обновление загружено. Подготовка к установке..."));

            if (!UpdaterLauncher.TryLaunchLocalPackage(
                    packagePath,
                    release.VersionText,
                    restart: true))
            {
                throw new InvalidOperationException(
                    "Не найден компонент обновления.");
            }

            reportProgress(new UpdateProgressState(
                true,
                100,
                false,
                "Установка обновления. Программа будет перезапущена..."));

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsApplyingUpdate = false;

            if (progressStarted)
            {
                reportProgress(new UpdateProgressState(
                    true,
                    0,
                    false,
                    $"Ошибка обновления: {ex.Message}"));
            }

            if (showErrors || progressStarted)
            {
                MessageBox.Show(
                    owner,
                    ex.Message,
                    "Ошибка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            CheckLock.Release();
        }
    }

    private static async Task<string> DownloadPackageAsync(
        ReleaseInfo release,
        Action<UpdateProgressState> reportProgress)
    {
        using var response = await DownloadHttp.GetAsync(
            release.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var downloadDir = Path.Combine(
            Path.GetTempPath(),
            "WOJD-Localization-Studio",
            "downloads");

        Directory.CreateDirectory(downloadDir);

        var packagePath = Path.Combine(
            downloadDir,
            $"WOJD-Localization-Studio-{release.VersionText}.zip");

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            packagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            useAsync: true);

        var buffer = new byte[128 * 1024];
        long received = 0;
        var lastPercent = -1;

        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read));
            received += read;

            if (totalBytes is > 0)
            {
                var percent = (int)Math.Clamp(
                    received * 100L / totalBytes.Value,
                    0,
                    100);

                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    reportProgress(new UpdateProgressState(
                        true,
                        percent,
                        false,
                        $"Скачивание обновления {release.VersionText}..."));
                }
            }
            else
            {
                reportProgress(new UpdateProgressState(
                    true,
                    0,
                    true,
                    $"Скачивание обновления {release.VersionText}..."));
            }
        }

        await output.FlushAsync();

        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            reportProgress(new UpdateProgressState(
                true,
                100,
                false,
                "Проверка загруженного обновления..."));

            await using var verifyStream = File.OpenRead(packagePath);
            var hash = await SHA256.HashDataAsync(verifyStream);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();

            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(packagePath); } catch { }

                throw new InvalidDataException(
                    "Контрольная сумма загруженного обновления не совпала.");
            }
        }

        return packagePath;
    }

    private static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using var response = await ApiHttp.GetAsync(LatestReleaseUrl);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var versionText = tag.TrimStart('v', 'V');

        if (!Version.TryParse(versionText, out var version))
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(
                    asset.GetProperty("name").GetString(),
                    PackageAssetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(url))
                continue;

            string? sha256 = null;

            if (asset.TryGetProperty("digest", out var digestElement))
            {
                var digest = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(digest) &&
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    sha256 = digest["sha256:".Length..];
                }
            }

            return new ReleaseInfo(
                version,
                versionText,
                url,
                sha256);
        }

        return null;
    }

    private static Version GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version
           ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateApiHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WOJD-Localization-Studio-Updater/1.0");

        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/vnd.github+json");

        return client;
    }

    private static HttpClient CreateDownloadHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WOJD-Localization-Studio-Updater/1.0");

        return client;
    }

    private sealed record ReleaseInfo(
        Version Version,
        string VersionText,
        string DownloadUrl,
        string? Sha256);
}

internal static class UpdaterLauncher
{
    public static bool TryLaunchLocalPackage(
        string packagePath,
        string version,
        bool restart)
    {
        var updater = Path.Combine(
            AppContext.BaseDirectory,
            "Updater",
            "WOJD-Localization-Studio.Updater.exe");

        if (!File.Exists(updater))
            return false;

        var appExe = Path.Combine(
            AppContext.BaseDirectory,
            "WOJD-Localization-Studio.exe");

        var info = new ProcessStartInfo(updater)
        {
            UseShellExecute = false
        };

        info.ArgumentList.Add("--apply");
        info.ArgumentList.Add("--install-dir");
        info.ArgumentList.Add(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

        info.ArgumentList.Add("--package-file");
        info.ArgumentList.Add(packagePath);

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
            var updater = Path.Combine(
                AppContext.BaseDirectory,
                "Updater",
                "WOJD-Localization-Studio.Updater.exe");

            if (!File.Exists(updater))
                return;

            var installDir =
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

            var taskCommand =
                $"\"{updater}\" --scheduled --install-dir \"{installDir}\"";

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
            info.ArgumentList.Add("1");
            info.ArgumentList.Add("/F");

            using var process = Process.Start(info);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Встроенная проверка обновлений продолжит работать,
            // даже если Планировщик Windows недоступен.
        }
    }
}
