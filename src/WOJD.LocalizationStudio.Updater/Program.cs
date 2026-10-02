using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
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

            Log(options.InstallDir, "Updater started.");

            if (!options.TempRun)
            {
                RelaunchFromTemp(args);
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
                var targetVersion = Version.Parse(options.VersionText);
                var localVersion = ReadLocalVersion(options.InstallDir);

                if (targetVersion <= localVersion)
                {
                    TryDelete(options.PackageFile);
                    return;
                }

                if (options.WaitPid is int directPid)
                    await WaitForProcessExitAsync(directPid, TimeSpan.FromMinutes(2));
                else if (IsMainAppRunning())
                    return;

                await VerifyPackageAsync(
                    options.PackageFile,
                    expectedSha256: null);

                await ApplyLocalPackageAsync(
                    options.PackageFile,
                    options.InstallDir);

                TryDelete(options.PackageFile);

                Log(options.InstallDir, $"Applied local package {options.VersionText}.");

                RestartIfNeeded(options);
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

            var currentVersion =
                ReadLocalVersion(options.InstallDir);

            Log(
                options.InstallDir,
                $"Current={currentVersion}; Latest={release.VersionText}");

            if (release.Version <= currentVersion)
                return;

            if (options.WaitPid is int pid)
                await WaitForProcessExitAsync(
                    pid,
                    TimeSpan.FromMinutes(2));
            else if (IsMainAppRunning())
                return;

            await DownloadAndApplyPackageAsync(
                release,
                options.InstallDir);

            Log(
                options.InstallDir,
                $"Applied scheduled update {release.VersionText}.");

            RestartIfNeeded(options);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(options.InstallDir))
                Log(options.InstallDir, $"Updater failed: {ex}");
        }
    }

    private static void RestartIfNeeded(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.RestartExe) ||
            !File.Exists(options.RestartExe))
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo(options.RestartExe)
            {
                UseShellExecute = true,
                WorkingDirectory = options.InstallDir
            });
    }

    private static void RelaunchFromTemp(string[] originalArgs)
    {
        var currentExe = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(currentExe) ||
            !File.Exists(currentExe))
        {
            return;
        }

        var tempDir =
            Path.Combine(
                Path.GetTempPath(),
                "WOJD-Localization-Studio",
                "updater");

        Directory.CreateDirectory(tempDir);

        var tempExe =
            Path.Combine(
                tempDir,
                $"updater-{Guid.NewGuid():N}.exe");

        File.Copy(
            currentExe,
            tempExe,
            true);

        var psi =
            new ProcessStartInfo(tempExe)
            {
                UseShellExecute = false
            };

        foreach (var arg in originalArgs)
            psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--temp-run");

        Process.Start(psi);
    }

    private static async Task<ReleaseInfo> GetLatestReleaseAsync()
    {
        using var http = CreateHttpClient();

        var url =
            $"{LatestManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.CacheControl =
            new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MaxAge = TimeSpan.Zero
            };

        request.Headers.Pragma.ParseAdd("no-cache");

        using var response =
            await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync();

        var manifest =
            await JsonSerializer.DeserializeAsync<UpdateManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.PackageUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException(
                "Invalid update.json manifest.");
        }

        if (!Version.TryParse(
                manifest.Version,
                out var version))
        {
            throw new InvalidDataException(
                $"Invalid manifest version: {manifest.Version}");
        }

        return new ReleaseInfo(
            version,
            manifest.Version,
            manifest.PackageUrl,
            manifest.Sha256);
    }

    private static Version ReadLocalVersion(
        string installDir)
    {
        try
        {
            var path =
                Path.Combine(
                    installDir,
                    "version.txt");

            return Version.TryParse(
                File.ReadAllText(path).Trim(),
                out var version)
                ? version
                : new Version(0, 0, 0, 0);
        }
        catch
        {
            return new Version(0, 0, 0, 0);
        }
    }

    private static async Task DownloadAndApplyPackageAsync(
        ReleaseInfo release,
        string installDir)
    {
        using var http = CreateHttpClient();

        var tempRoot =
            Path.Combine(
                Path.GetTempPath(),
                "WOJD-Localization-Studio",
                Guid.NewGuid().ToString("N"));

        var zipPath =
            Path.Combine(
                tempRoot,
                "update.zip");

        Directory.CreateDirectory(tempRoot);

        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    release.DownloadUrl);

            request.Headers.CacheControl =
                new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };

            using var response =
                await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            await using (var input =
                         await response.Content.ReadAsStreamAsync())
            await using (var output =
                         File.Create(zipPath))
            {
                await input.CopyToAsync(output);
            }

            await VerifyPackageAsync(
                zipPath,
                release.Sha256);

            await ApplyLocalPackageAsync(
                zipPath,
                installDir);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    tempRoot,
                    true);
            }
            catch
            {
            }
        }
    }

    private static async Task VerifyPackageAsync(
        string path,
        string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return;

        await using var stream =
            File.OpenRead(path);

        var hash =
            await SHA256.HashDataAsync(stream);

        var actual =
            Convert
                .ToHexString(hash)
                .ToLowerInvariant();

        if (!string.Equals(
                actual,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Downloaded update hash mismatch.");
        }
    }

    private static Task ApplyLocalPackageAsync(
        string zipPath,
        string installDir)
    {
        var tempRoot =
            Path.Combine(
                Path.GetTempPath(),
                "WOJD-Localization-Studio",
                Guid.NewGuid().ToString("N"));

        var extractDir =
            Path.Combine(
                tempRoot,
                "files");

        Directory.CreateDirectory(
            extractDir);

        try
        {
            ZipFile.ExtractToDirectory(
                zipPath,
                extractDir,
                overwriteFiles: true);

            Directory.CreateDirectory(
                installDir);

            foreach (var source in
                     Directory.EnumerateFiles(
                         extractDir,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relative =
                    Path.GetRelativePath(
                        extractDir,
                        source);

                var destination =
                    Path.Combine(
                        installDir,
                        relative);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(destination)!);

                File.Copy(
                    source,
                    destination,
                    true);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(
                    tempRoot,
                    true);
            }
            catch
            {
            }
        }

        return Task.CompletedTask;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler =
            new HttpClientHandler
            {
                AllowAutoRedirect = true
            };

        var http =
            new HttpClient(handler)
            {
                Timeout =
                    TimeSpan.FromMinutes(5)
            };

        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WOJD-Localization-Studio-Updater/1.0");

        return http;
    }

    private static bool IsMainAppRunning()
        => Process
            .GetProcessesByName(
                "WOJD-Localization-Studio")
            .Any(
                p =>
                    p.Id !=
                    Environment.ProcessId);

    private static async Task WaitForProcessExitAsync(
        int pid,
        TimeSpan timeout)
    {
        try
        {
            using var process =
                Process.GetProcessById(pid);

            using var cts =
                new CancellationTokenSource(timeout);

            await process.WaitForExitAsync(
                cts.Token);
        }
        catch
        {
        }
    }

    private static void TryDelete(
        string path)
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

    private static void Log(
        string installDir,
        string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(
                    installDir,
                    "update.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Updater] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed record UpdateManifest(
        string Version,
        string PackageUrl,
        string Sha256);

    private sealed record ReleaseInfo(
        Version Version,
        string VersionText,
        string DownloadUrl,
        string? Sha256);

    private sealed class Options
    {
        public string InstallDir { get; private set; } =
            string.Empty;

        public string? PackageUrl { get; private set; }
        public string? PackageFile { get; private set; }
        public string? VersionText { get; private set; }
        public string? RestartExe { get; private set; }
        public int? WaitPid { get; private set; }
        public bool Scheduled { get; private set; }
        public bool TempRun { get; private set; }

        public static Options Parse(
            string[] args)
        {
            var result =
                new Options();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--install-dir"
                        when i + 1 < args.Length:
                        result.InstallDir =
                            args[++i];
                        break;

                    case "--package-url"
                        when i + 1 < args.Length:
                        result.PackageUrl =
                            args[++i];
                        break;

                    case "--package-file"
                        when i + 1 < args.Length:
                        result.PackageFile =
                            args[++i];
                        break;

                    case "--version"
                        when i + 1 < args.Length:
                        result.VersionText =
                            args[++i];
                        break;

                    case "--restart"
                        when i + 1 < args.Length:
                        result.RestartExe =
                            args[++i];
                        break;

                    case "--wait-pid"
                        when i + 1 < args.Length &&
                             int.TryParse(
                                 args[++i],
                                 out var pid):
                        result.WaitPid =
                            pid;
                        break;

                    case "--scheduled":
                        result.Scheduled =
                            true;
                        break;

                    case "--temp-run":
                        result.TempRun =
                            true;
                        break;
                }
            }

            return result;
        }
    }
}
