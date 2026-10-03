using System.Diagnostics;
using System.IO;
using System.Text;
namespace WOJD.LocalizationStudio.Services;
public sealed class GitChange
{
    public bool Include { get; set; }
    public string Status { get; init; } = "";
    public string Path { get; init; } = "";
    public string? OriginalPath { get; init; }
    public string? Hash { get; init; }
}
public static class GitService
{
    public static async Task<string> RunAsync(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("--literal-pathspecs"); start.ArgumentList.Add("-c"); start.ArgumentList.Add("color.ui=false"); start.ArgumentList.Add("-c"); start.ArgumentList.Add("core.quotepath=false");
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить Git.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new IOException("Git превысил время ожидания."); }
        var result = await output; var diagnostic = await error;
        if (process.ExitCode != 0) throw new IOException("Git: " + diagnostic + result);
        return result;
    }
    public static async Task<string> RootAsync(string path) => (await RunAsync(path, "rev-parse", "--show-toplevel")).Trim();
    public static string Relative(string root, string path)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
        var prefix = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || full.StartsWith(System.IO.Path.Combine(root, ".git") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Путь вне рабочей копии Git.");
        return System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');
    }
    public static async Task<List<GitChange>> StatusAsync(string root)
    {
        var parts = (await RunAsync(root, "status", "--porcelain=v1", "-z", "--untracked-files=all")).Split('\0');
        var changes = new List<GitChange>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length < 4) continue;
            var status = parts[i][..2]; var path = Relative(root, parts[i][3..]); string? original = null;
            if (status.Contains('R') || status.Contains('C')) { if (++i >= parts.Length) throw new IOException("Повреждён ответ Git."); original = Relative(root, parts[i]); }
            var full = System.IO.Path.Combine(root, path);
            changes.Add(new() { Status = status, Path = path, OriginalPath = original, Hash = File.Exists(full) ? FileSafetyService.Hash(full) : null });
        }
        return changes;
    }
    public static async Task<string> CommitAsync(string root, IEnumerable<GitChange> selected, string message)
    {
        var list = selected.ToList(); if (list.Count == 0 || string.IsNullOrWhiteSpace(message)) throw new IOException("Выберите файлы и введите сообщение.");
        var current = await StatusAsync(root);
        foreach (var item in list)
            if (!current.Any(c => c.Path == item.Path && c.Status == item.Status && c.Hash == item.Hash && c.OriginalPath == item.OriginalPath))
                throw new IOException("Статус/содержимое изменилось. Обновите просмотр перед коммитом.");
        if (list.Any(i => i.Status.Contains('U') || i.Status is "AA" or "DD")) throw new IOException("Сначала разрешите конфликты Git.");
        var paths = list.SelectMany(i => i.OriginalPath is null ? new[] { Relative(root, i.Path) } : new[] { Relative(root, i.Path), Relative(root, i.OriginalPath) }).Distinct().ToArray();
        await RunAsync(root, new[] { "add", "--" }.Concat(paths).ToArray());
        return await RunAsync(root, new[] { "commit", "--only", "-m", message, "--" }.Concat(paths).ToArray());
    }
    public static async Task SwitchAsync(string root, string name, bool create)
    {
        await RunAsync(root, "check-ref-format", "--branch", name);
        if (create) await RunAsync(root, "switch", "-c", name); else await RunAsync(root, "switch", "--", name);
    }
}
