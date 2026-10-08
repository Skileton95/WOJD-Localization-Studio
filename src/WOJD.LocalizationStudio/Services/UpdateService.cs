using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private const string LatestManifestUrl =
        "https://github.com/Skileton95/WOJD-Localization-Studio/releases/latest/download/update.json";

    private static readonly HttpClient Http = CreateHttpClient();
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

        UpdateDiagnostics.Write(
            $"Startup check. Current version: {GetCurrentVersion().ToString(3)}");

        await CheckAndPromptAsync(
            owner,
            reportProgress,
            confirmDiscardUnsaved,
            isManual: false);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };

        _timer.Tick += async (_, _) =>
            await CheckAndPromptAsync(
                owner,
                reportProgress,
                confirmDiscardUnsaved,
                isManual: false);

        _timer.Start();
    }

    public static Task CheckNowAsync(
        Window owner,
        Action<UpdateProgressState> reportProgress,
        Func<bool> confirmDiscardUnsaved)
        => CheckAndPromptAsync(
            owner,
            reportProgress,
            confirmDiscardUnsaved,
            isManual: true);

    private static async Task CheckAndPromptAsync(
        Window owner,
        Action<UpdateProgressState> reportProgress,
        Func<bool> confirmDiscardUnsaved,
        bool isManual)
    {
        if (IsApplyingUpdate)
        {
            if (isManual)
            {
                AppDialog.Show(
                    "Обновление уже выполняется.",
                    "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    owner);
            }

            return;
        }

        if (!await CheckLock.WaitAsync(0))
        {
            if (isManual)
            {
                AppDialog.Show(
                    "Проверка обновлений уже выполняется.",
                    "Проверка обновлений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    owner);
            }

            return;
        }

        var progressStarted = false;

        try
        {
            var current = GetCurrentVersion();

            UpdateDiagnostics.Write(
                $"Checking updates. Manual={isManual}; Current={current.ToString(3)}");

            var release = await GetLatestReleaseAsync();

            UpdateDiagnostics.Write(
                $"Manifest received. Latest={release.VersionText}; Package={release.DownloadUrl}");

            if (release.Version <= current)
            {
                if (isManual)
                {
                    AppDialog.Show(
                        $"Установлена последняя версия v{current.ToString(3)}.",
                        "Проверка обновлений",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information,
                        owner);
                }

                return;
            }

            if (!isManual && release.VersionText == _ignoredVersion)
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

            reportProgress(
                new UpdateProgressState(
                    true,
                    0,
                    true,
                    $"Подготовка обновления {release.VersionText}..."));

            var packagePath = await DownloadPackageAsync(release, reportProgress);

            reportProgress(
                new UpdateProgressState(
                    true,
                    100,
                    false,
                    "Обновление загружено. Подготовка установщика..."));

            if (!UpdaterLauncher.TryLaunchLocalPackage(
                    packagePath,
                    release.VersionText,
                    restart: true,
                    out var launchError))
            {
                throw new InvalidOperationException(
                    launchError ?? "Не удалось запустить компонент обновления.");
            }

            UpdateDiagnostics.Write(
                $"Staged updater launched for version {release.VersionText}. Application may now shut down safely.");

            reportProgress(
                new UpdateProgressState(
                    true,
                    100,
                    false,
                    "Установщик запущен. Программа будет закрыта и запущена снова..."));

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsApplyingUpdate = false;

            UpdateDiagnostics.Write($"Update check failed: {ex}");

            if (progressStarted)
            {
                reportProgress(
                    new UpdateProgressState(
                        true,
                        0,
                        false,
                        $"Ошибка обновления: {ex.Message}"));
            }

            if (isManual || progressStarted)
            {
                AppDialog.Show(
                    $"{ex.Message}\n\nПрограмма не будет закрыта. Подробности записаны в update.log.",
                    "Ошибка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    owner);
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
        using var request = CreateNoCacheRequest(release.DownloadUrl);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var installDir = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var downloadDir = Path.Combine(installDir, ".update", "packages");

        Directory.CreateDirectory(downloadDir);
        CleanupOldPackages(downloadDir);

        var packagePath = Path.Combine(
            downloadDir,
            $"WOJD-Localization-Studio-{release.VersionText}-{Guid.NewGuid():N}.zip");

        UpdateDiagnostics.Write($"Downloading update into local staging: {packagePath}");

        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(
                         packagePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         useAsync: true))
        {
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
                        reportProgress(
                            new UpdateProgressState(
                                true,
                                percent,
                                false,
                                $"Скачивание обновления {release.VersionText}..."));
                    }
                }
                else
                {
                    reportProgress(
                        new UpdateProgressState(
                            true,
                            0,
                            true,
                            $"Скачивание обновления {release.VersionText}..."));
                }
            }

            await output.FlushAsync();
        }

        reportProgress(
            new UpdateProgressState(
                true,
                100,
                false,
                "Проверка загруженного обновления..."));

        await using var verifyStream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(verifyStream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(packagePath);
            throw new InvalidDataException(
                "Контрольная сумма загруженного обновления не совпала.");
        }

        UpdateDiagnostics.Write($"Package downloaded and verified: {packagePath}");
        return packagePath;
    }

    private static void CleanupOldPackages(string downloadDir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(downloadDir, "*.zip"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(12))
                        File.Delete(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static async Task<ReleaseInfo> GetLatestReleaseAsync()
    {
        var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{LatestManifestUrl}?t={cacheBust}";

        using var request = CreateNoCacheRequest(url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Сервер обновлений вернул HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.PackageUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException("Файл update.json имеет неверный формат.");
        }

        if (!Version.TryParse(manifest.Version, out var version))
            throw new InvalidDataException($"Некорректная версия в update.json: {manifest.Version}");

        return new ReleaseInfo(version, manifest.Version, manifest.PackageUrl, manifest.Sha256);
    }

    private static HttpRequestMessage CreateNoCacheRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
        request.Headers.Pragma.ParseAdd("no-cache");
        return request;
    }

    private static Version GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version
           ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio-Updater/1.0");
        return client;
    }

    private sealed record UpdateManifest(string Version, string PackageUrl, string Sha256);

    private sealed record ReleaseInfo(
        Version Version,
        string VersionText,
        string DownloadUrl,
        string Sha256);
}

internal static class UpdateDiagnostics
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "update.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}

internal static class UpdaterLauncher
{
    public static bool TryLaunchLocalPackage(
        string packagePath,
        string version,
        bool restart,
        out string? error)
    {
        error = null;

        try
        {
            var installDir = AppContext.BaseDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var updater = Path.Combine(
                installDir,
                "Updater",
                "WOJD-Localization-Studio.Updater.exe");

            if (!File.Exists(updater))
            {
                error = "Не найден компонент обновления Updater\\WOJD-Localization-Studio.Updater.exe.";
                return false;
            }

            if (!File.Exists(packagePath))
            {
                error = "Загруженный пакет обновления не найден.";
                return false;
            }

            var runnerDir = Path.Combine(installDir, ".update", "runner");
            Directory.CreateDirectory(runnerDir);
            CleanupOldRunners(runnerDir);

            var runner = Path.Combine(
                runnerDir,
                $"updater-{Guid.NewGuid():N}.exe");

            File.Copy(updater, runner, true);

            if (new FileInfo(updater).Length != new FileInfo(runner).Length)
            {
                TryDelete(runner);
                error = "Не удалось корректно подготовить компонент обновления.";
                return false;
            }

            var appExe = Path.Combine(installDir, "WOJD-Localization-Studio.exe");
            var info = new ProcessStartInfo(runner)
            {
                UseShellExecute = false,
                WorkingDirectory = installDir
            };

            info.ArgumentList.Add("--temp-run");
            info.ArgumentList.Add("--install-dir");
            info.ArgumentList.Add(installDir);
            info.ArgumentList.Add("--package-file");
            info.ArgumentList.Add(Path.GetFullPath(packagePath));
            info.ArgumentList.Add("--version");
            info.ArgumentList.Add(version);
            info.ArgumentList.Add("--wait-pid");
            info.ArgumentList.Add(Environment.ProcessId.ToString());

            if (restart)
            {
                info.ArgumentList.Add("--restart");
                info.ArgumentList.Add(appExe);
            }

            var process = Process.Start(info);
            if (process is null)
            {
                TryDelete(runner);
                error = "Windows не смог запустить компонент обновления.";
                return false;
            }

            UpdateDiagnostics.Write(
                $"Updater staged before shutdown. PID={process.Id}; Runner={runner}; Package={packagePath}");

            return true;
        }
        catch (Exception ex)
        {
            error = $"Не удалось подготовить установщик обновления: {ex.Message}";
            UpdateDiagnostics.Write($"Updater staging failed: {ex}");
            return false;
        }
    }

    private static void CleanupOldRunners(string runnerDir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(runnerDir, "updater-*.exe"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1))
                        File.Delete(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
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

            var installDir = AppContext.BaseDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            info.ArgumentList.Add("1");
            info.ArgumentList.Add("/F");

            using var process = Process.Start(info);
            process?.WaitForExit(5000);

            UpdateDiagnostics.Write(
                $"Scheduled updater task registration exit code: {process?.ExitCode}");
        }
        catch (Exception ex)
        {
            UpdateDiagnostics.Write($"Scheduled updater registration failed: {ex}");
        }
    }
}
