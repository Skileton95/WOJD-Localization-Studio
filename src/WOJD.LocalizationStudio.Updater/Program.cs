using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Updater;

internal static class Program
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Skileton95/WOJD-Localization-Studio/releases/latest";
    private const string PackageAssetName = "WOJD-Localization-Studio-win-x64.zip";

    [STAThread]
    private static async Task Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (string.IsNullOrWhiteSpace(options.InstallDir)) return;

            if (!options.TempRun)
            {
                RelaunchFromTemp(args);
                return;
            }

            if (options.Scheduled && IsMainAppRunning()) return;

            var release = options.PackageUrl is not null && options.VersionText is not null
                ? new ReleaseInfo(Version.Parse(options.VersionText), options.VersionText, options.PackageUrl)
                : await GetLatestReleaseAsync();

            if (release is null) return;
            var localVersion = ReadLocalVersion(options.InstallDir);
            if (release.Version <= localVersion) return;

            if (options.WaitPid is int pid)
                await WaitForProcessExitAsync(pid, TimeSpan.FromMinutes(2));
            else if (IsMainAppRunning())
                return;

            await ApplyPackageAsync(release.DownloadUrl, options.InstallDir);

            if (!string.IsNullOrWhiteSpace(options.RestartExe) && File.Exists(options.RestartExe))
                Process.Start(new ProcessStartInfo(options.RestartExe) { UseShellExecute = true, WorkingDirectory = options.InstallDir });
        }
        catch
        {
            // Фоновое обновление не должно показывать окна или мешать запуску редактора.
        }
    }

    private static void RelaunchFromTemp(string[] originalArgs)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "WOJD-Localization-Studio", "updater");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, $"updater-{Guid.NewGuid():N}.exe");
        File.Copy(currentExe, tempExe, true);

        var psi = new ProcessStartInfo(tempExe) { UseShellExecute = false };
        foreach (var arg in originalArgs) psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("--temp-run");
        Process.Start(psi);
    }

    private static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio-Updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.GetAsync(LatestReleaseUrl);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var text = tag.TrimStart('v', 'V');
        if (!Version.TryParse(text, out var version)) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), PackageAssetName, StringComparison.OrdinalIgnoreCase)) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrWhiteSpace(url)) return new ReleaseInfo(version, text, url);
        }
        return null;
    }

    private static Version ReadLocalVersion(string installDir)
    {
        try
        {
            var path = Path.Combine(installDir, "version.txt");
            return Version.TryParse(File.ReadAllText(path).Trim(), out var version) ? version : new Version(0, 0, 0, 0);
        }
        catch
        {
            return new Version(0, 0, 0, 0);
        }
    }

    private static async Task ApplyPackageAsync(string url, string installDir)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WOJD-Localization-Studio-Updater/1.0");

        var tempRoot = Path.Combine(Path.GetTempPath(), "WOJD-Localization-Studio", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, "update.zip");
        var extractDir = Path.Combine(tempRoot, "files");
        Directory.CreateDirectory(extractDir);

        try
        {
            await using (var input = await http.GetStreamAsync(url))
            await using (var output = File.Create(zipPath))
                await input.CopyToAsync(output);

            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            Directory.CreateDirectory(installDir);

            foreach (var source in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extractDir, source);
                var destination = Path.Combine(installDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static bool IsMainAppRunning()
        => Process.GetProcessesByName("WOJD-Localization-Studio").Any(p => p.Id != Environment.ProcessId);

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
        }
        catch
        {
            // Уже завершён или истёк таймаут.
        }
    }

    private sealed record ReleaseInfo(Version Version, string VersionText, string DownloadUrl);

    private sealed class Options
    {
        public string InstallDir { get; private set; } = string.Empty;
        public string? PackageUrl { get; private set; }
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
                    case "--install-dir" when i + 1 < args.Length: result.InstallDir = args[++i]; break;
                    case "--package-url" when i + 1 < args.Length: result.PackageUrl = args[++i]; break;
                    case "--version" when i + 1 < args.Length: result.VersionText = args[++i]; break;
                    case "--restart" when i + 1 < args.Length: result.RestartExe = args[++i]; break;
                    case "--wait-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid): result.WaitPid = pid; break;
                    case "--scheduled": result.Scheduled = true; break;
                    case "--temp-run": result.TempRun = true; break;
                }
            }
            return result;
        }
    }
}
