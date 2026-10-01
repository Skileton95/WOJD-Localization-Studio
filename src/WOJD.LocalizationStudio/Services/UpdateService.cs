using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
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

    public async Task PrepareUpdateAsync(
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = FindRepositoryRoot()
            ?? throw new InvalidOperationException(
                "Не найдена папка Git-репозитория. Запустите программу из клонированной папки WOJD-Localization-Studio.");

        var solution = Path.Combine(root, "WOJD.LocalizationStudio.sln");
        var project = Path.Combine(root, "src", "WOJD.LocalizationStudio", "WOJD.LocalizationStudio.csproj");
        var targetDirectory = Path.Combine(root, "src", "WOJD.LocalizationStudio", "bin", "Release", "net8.0-windows");
        var stageDirectory = Path.Combine(root, ".update-stage");

        progress?.Report(new UpdateProgress(5, "Подготовка обновления"));

        var status = await RunProcessAsync(
            "git",
            "status --porcelain",
            root,
            captureOutput: true,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(status.Output))
            throw new InvalidOperationException(
                "В исходниках есть локальные изменения. Сначала сохраните их в Git или отмените, затем повторите обновление.");

        progress?.Report(new UpdateProgress(15, "Загрузка обновления"));
        await RunProcessAsync(
            "git",
            "pull --ff-only origin main",
            root,
            captureOutput: false,
            cancellationToken);

        progress?.Report(new UpdateProgress(35, "Восстановление зависимостей"));
        await RunProcessAsync(
            "dotnet",
            $"restore \"{solution}\"",
            root,
            captureOutput: false,
            cancellationToken);

        if (Directory.Exists(stageDirectory))
            Directory.Delete(stageDirectory, true);

        Directory.CreateDirectory(stageDirectory);

        progress?.Report(new UpdateProgress(55, "Сборка новой версии"));
        await RunProcessAsync(
            "dotnet",
            $"build \"{project}\" -c Release --no-restore -o \"{stageDirectory}\"",
            root,
            captureOutput: false,
            cancellationToken);

        var stagedExe = Path.Combine(stageDirectory, "WOJD.LocalizationStudio.exe");
        if (!File.Exists(stagedExe))
            throw new InvalidOperationException("Сборка завершилась, но новый EXE не найден.");

        progress?.Report(new UpdateProgress(88, "Подготовка установки"));
        LaunchApplyHelper(root, stageDirectory, targetDirectory);

        progress?.Report(new UpdateProgress(100, "Перезапуск"));
    }

    private static void LaunchApplyHelper(string root, string stageDirectory, string targetDirectory)
    {
        var helperPath = Path.Combine(root, ".apply-update.cmd");
        var appPath = Path.Combine(targetDirectory, "WOJD.LocalizationStudio.exe");

        var script = $"""
@echo off
setlocal
set "STAGE={stageDirectory}"
set "TARGET={targetDirectory}"
set "APP={appPath}"

timeout /t 1 /nobreak >nul

:wait_for_app
tasklist /FI "IMAGENAME eq WOJD.LocalizationStudio.exe" 2>nul | find /I "WOJD.LocalizationStudio.exe" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait_for_app
)

if not exist "%TARGET%" mkdir "%TARGET%"
xcopy "%STAGE%\*" "%TARGET%\" /E /I /Y /Q >nul
if errorlevel 2 exit /b 1

rmdir /S /Q "%STAGE%" >nul 2>&1
start "" "%APP%"
del "%~f0" >nul 2>&1
""";

        File.WriteAllText(helperPath, script, new UTF8Encoding(false));

        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        Process.Start(new ProcessStartInfo
        {
            FileName = commandProcessor,
            Arguments = $"/d /c \"\"{helperPath}\"\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 10 && directory is not null; i++, directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
        }

        return null;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        bool captureOutput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Не удалось запустить {fileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                $"{fileName} завершился с кодом {process.ExitCode}.\n{details.Trim()}");
        }

        return new ProcessResult(captureOutput ? output : string.Empty);
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

    private sealed record ProcessResult(string Output);
}

public sealed record UpdateInfo(string Version, string Notes);
public sealed record UpdateProgress(int Percent, string Message);
