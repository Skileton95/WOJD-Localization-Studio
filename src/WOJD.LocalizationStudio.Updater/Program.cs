using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Updater;

internal static class Program
{
    private const string LatestManifestUrl =
        "https://github.com/Skileton95/WOJD-Localization-Studio/releases/latest/download/update.json";

    [STAThread]
    private static async Task Main(string[] args)
    {
        var options = Options.Parse(args);

        try
        {
            if (string.IsNullOrWhiteSpace(options.InstallDir))
                return;

            options.InstallDir = Path.GetFullPath(options.InstallDir);
            Directory.CreateDirectory(options.InstallDir);
            Log(options.InstallDir, $"Updater started. TempRun={options.TempRun}; Scheduled={options.Scheduled}");

            if (!options.TempRun)
            {
                RelaunchFromStaging(args, options.InstallDir);
                return;
            }

            if (options.Scheduled && IsMainAppRunning())
            {
                Log(options.InstallDir, "Scheduled check skipped because main app is running.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.PackageFile) &&
                !string.IsNullOrWhiteSpace(options.VersionText))
            {
                await ApplyDownloadedPackageAsync(options);
                return;
            }

            var release =
                options.PackageUrl is not null &&
                options.VersionText is not null
                    ? new ReleaseInfo(
                        Version.Parse(options.VersionText),
                        options.VersionText,
                        options.PackageUrl,
                        null)
                    : await GetLatestReleaseAsync();

            var currentVersion = ReadLocalVersion(options.InstallDir);
            Log(options.InstallDir, $"Current={currentVersion}; Latest={release.VersionText}");

            if (release.Version <= currentVersion)
                return;

            if (options.WaitPid is int pid)
                await WaitForProcessExitAsync(pid, TimeSpan.FromMinutes(2));
            else if (IsMainAppRunning())
                return;

            await DownloadAndApplyPackageAsync(release, options.InstallDir);
            VerifyInstalledVersion(options.InstallDir, release.Version);

            Log(options.InstallDir, $"Applied scheduled update {release.VersionText}.");
            RestartIfNeeded(options);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(options.InstallDir))
                Log(options.InstallDir, $"Updater failed: {ex}");

            if (!options.Scheduled)
            {
                ShowError(
                    "Не удалось установить обновление WOJD Localization Studio.\n\n" +
                    ex.Message +
                    "\n\nПрограмма будет запущена снова. Подробности находятся в update.log.");

                RestartIfNeeded(options);
            }
        }
    }

    private static async Task ApplyDownloadedPackageAsync(Options options)
    {
        var targetVersion = Version.Parse(options.VersionText!);
        var localVersion = ReadLocalVersion(options.InstallDir);
        var packageFile = Path.GetFullPath(options.PackageFile!);

        Log(
            options.InstallDir,
            $"Local package requested. Current={localVersion}; Target={targetVersion}; Package={packageFile}");

        if (targetVersion <= localVersion)
        {
            TryDelete(packageFile);
            Log(options.InstallDir, "Package is not newer; restarting application without changes.");
            RestartIfNeeded(options);
            return;
        }

        if (options.WaitPid is int directPid)
            await WaitForProcessExitAsync(directPid, TimeSpan.FromMinutes(2));
        else if (IsMainAppRunning())
            throw new InvalidOperationException("Основная программа всё ещё запущена.");

        await VerifyPackageAsync(packageFile, expectedSha256: null);
        await ApplyLocalPackageAsync(packageFile, options.InstallDir);
        VerifyInstalledVersion(options.InstallDir, targetVersion);

        TryDelete(packageFile);
        Log(options.InstallDir, $"Applied local package {options.VersionText}.");

        RestartIfNeeded(options);
    }

    private static bool RestartIfNeeded(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.RestartExe) ||
            !File.Exists(options.RestartExe))
        {
            return false;
        }

        try
        {
            var process = Process.Start(
                new ProcessStartInfo(options.RestartExe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = options.InstallDir
                });

            Log(options.InstallDir, process is null
                ? "Application restart returned no process."
                : $"Application restarted. PID={process.Id}");

            return process is not null;
        }
        catch (Exception ex)
        {
            Log(options.InstallDir, $"Application restart failed: {ex}");
            return false;
        }
    }

    private static void RelaunchFromStaging(string[] originalArgs, string installDir)
    {
        var currentExe = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(currentExe) ||
            !File.Exists(currentExe))
        {
            throw new FileNotFoundException("Не найден исполняемый файл updater.");
        }

        var runnerDir = Path.Combine(installDir, ".update", "runner");
        Directory.CreateDirectory(runnerDir);
        CleanupOldRunners(runnerDir);

        var stagedExe = Path.Combine(
            runnerDir,
            $"updater-{Guid.NewGuid():N}.exe");

        File.Copy(currentExe, stagedExe, true);

        var sourceLength = new FileInfo(currentExe).Length;
        var stagedLength = new FileInfo(stagedExe).Length;
        if (sourceLength != stagedLength)
            throw new IOException("Не удалось корректно подготовить updater для запуска.");

        var psi = new ProcessStartInfo(stagedExe)
        {
            UseShellExecute = false,
            WorkingDirectory = installDir
        };

        foreach (var arg in originalArgs)
            psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--temp-run");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить подготовленный updater.");

        Log(installDir, $"Updater staged and relaunched. PID={process.Id}; Path={stagedExe}");
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

    private static async Task<ReleaseInfo> GetLatestReleaseAsync()
    {
        using var http = CreateHttpClient();
        var url = $"{LatestManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
        request.Headers.Pragma.ParseAdd("no-cache");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.PackageUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException("Invalid update.json manifest.");
        }

        if (!Version.TryParse(manifest.Version, out var version))
            throw new InvalidDataException($"Invalid manifest version: {manifest.Version}");

        return new ReleaseInfo(version, manifest.Version, manifest.PackageUrl, manifest.Sha256);
    }

    private static Version ReadLocalVersion(string installDir)
    {
        try
        {
            var path = Path.Combine(installDir, "version.txt");
            return Version.TryParse(File.ReadAllText(path).Trim(), out var version)
                ? version
                : new Version(0, 0, 0, 0);
        }
        catch
        {
            return new Version(0, 0, 0, 0);
        }
    }

    private static void VerifyInstalledVersion(string installDir, Version expected)
    {
        var installed = ReadLocalVersion(installDir);
        if (installed != expected)
        {
            throw new InvalidDataException(
                $"После установки версия не совпала. Ожидалась {expected}, обнаружена {installed}.");
        }
    }

    private static async Task DownloadAndApplyPackageAsync(ReleaseInfo release, string installDir)
    {
        using var http = CreateHttpClient();
        var packageDir = Path.Combine(installDir, ".update", "packages");
        Directory.CreateDirectory(packageDir);

        var zipPath = Path.Combine(
            packageDir,
            $"update-{release.VersionText}-{Guid.NewGuid():N}.zip");

        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(
                         zipPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         useAsync: true))
        {
            await input.CopyToAsync(output);
            await output.FlushAsync();
        }

        await VerifyPackageAsync(zipPath, release.Sha256);
        await ApplyLocalPackageAsync(zipPath, installDir);
        TryDelete(zipPath);
    }

    private static async Task VerifyPackageAsync(string path, string? expectedSha256)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл обновления не найден.", path);

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return;

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded update hash mismatch.");
    }

    private static async Task ApplyLocalPackageAsync(string zipPath, string installDir)
    {
        var installRoot = Path.GetFullPath(installDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installPrefix = installRoot + Path.DirectorySeparatorChar;
        var appliedFiles = 0;

        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var destination = Path.GetFullPath(Path.Combine(installRoot, relative));
            if (!destination.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Недопустимый путь в пакете обновления: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var tempDestination = destination + $".wojd-update-{Guid.NewGuid():N}.tmp";

            try
            {
                await using (var source = entry.Open())
                await using (var target = new FileStream(
                                 tempDestination,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 128 * 1024,
                                 useAsync: true))
                {
                    await source.CopyToAsync(target);
                    await target.FlushAsync();
                }

                await ReplaceFileWithRetryAsync(tempDestination, destination);
                appliedFiles++;
            }
            finally
            {
                TryDelete(tempDestination);
            }
        }

        Log(installDir, $"Package applied successfully. Files replaced: {appliedFiles}.");
    }

    private static async Task ReplaceFileWithRetryAsync(string source, string destination)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    try
                    {
                        File.SetAttributes(destination, FileAttributes.Normal);
                    }
                    catch
                    {
                    }
                }

                File.Move(source, destination, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < 20)
                    await Task.Delay(250);
            }
        }

        throw new IOException($"Не удалось заменить файл: {destination}", lastError);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio-Updater/1.0");
        return http;
    }

    private static bool IsMainAppRunning()
        => Process.GetProcessesByName("WOJD-Localization-Studio")
            .Any(p => p.Id != Environment.ProcessId);

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var cts = new CancellationTokenSource(timeout))
        {
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Основная программа не завершилась вовремя. Обновление не применено.");
            }
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

    private static void Log(string installDir, string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(installDir, "update.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Updater] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void ShowError(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "Ошибка обновления", 0x00000010u | 0x00000000u);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private sealed record UpdateManifest(string Version, string PackageUrl, string Sha256);

    private sealed record ReleaseInfo(
        Version Version,
        string VersionText,
        string DownloadUrl,
        string? Sha256);

    private sealed class Options
    {
        public string InstallDir { get; set; } = string.Empty;
        public string? PackageUrl { get; private set; }
        public string? PackageFile { get; private set; }
        public string? VersionText { get; private set; }
        public string? RestartExe { get; private set; }
        public int? WaitPid { get; private set; }
        public bool Scheduled { get; private set; }
        public bool TempRun { get; private set; }

        public static Options Parse(string[] args)
        {
            var result = new Options();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--install-dir" when i + 1 < args.Length:
                        result.InstallDir = args[++i];
                        break;
                    case "--package-url" when i + 1 < args.Length:
                        result.PackageUrl = args[++i];
                        break;
                    case "--package-file" when i + 1 < args.Length:
                        result.PackageFile = args[++i];
                        break;
                    case "--version" when i + 1 < args.Length:
                        result.VersionText = args[++i];
                        break;
                    case "--restart" when i + 1 < args.Length:
                        result.RestartExe = args[++i];
                        break;
                    case "--wait-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid):
                        result.WaitPid = pid;
                        break;
                    case "--scheduled":
                        result.Scheduled = true;
                        break;
                    case "--temp-run":
                        result.TempRun = true;
                        break;
                }
            }

            return result;
        }
    }
}
