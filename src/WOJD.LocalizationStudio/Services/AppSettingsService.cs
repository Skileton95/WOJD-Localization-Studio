using System.IO;
using System.Text.Json;
namespace WOJD.LocalizationStudio.Services;
public sealed class ShortcutSetting
{
    public string Action { get; set; } = "";
    public string Gesture { get; set; } = "";
}
public sealed class EditorSettings
{
    public double FontSize { get; set; } = 14;
    public int SearchDebounceMs { get; set; } = 220;
    public int BackupLimit { get; set; } = 50;
    public List<ShortcutSetting> Shortcuts { get; set; } = Defaults();
    public static List<ShortcutSetting> Defaults() => new[] { ("OpenFile", "Ctrl+O"), ("OpenFolder", "Ctrl+Shift+O"), ("Save", "Ctrl+S"), ("SaveAll", "Ctrl+Shift+S"), ("Undo", "Ctrl+Z"), ("Redo", "Ctrl+Y"), ("ToggleReplace", "Ctrl+H"), ("NextUntranslated", "F6"), ("PreviousUntranslated", "Shift+F6"), ("Apply", "Ctrl+Enter") }
        .Select(x => new ShortcutSetting { Action = x.Item1, Gesture = x.Item2 }).ToList();
}
public static class AppSettingsService
{
    private static string PathName => Path.Combine(WorkspaceStateService.StorageDirectory, "settings.json");
    public static EditorSettings Load()
    {
        try { return Validate(JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(PathName)) ?? new()); }
        catch (Exception e) { IssueLogService.Record("Настройки: " + e.Message); return new(); }
    }
    public static EditorSettings Validate(EditorSettings value)
    {
        if (!double.IsFinite(value.FontSize) || value.FontSize < 9 || value.FontSize > 28 ||
            value.SearchDebounceMs < 100 || value.SearchDebounceMs > 2000 || value.BackupLimit < 1 || value.BackupLimit > 500)
            throw new ArgumentException("Шрифт: 9–28; задержка поиска: 100–2000 мс; резервные копии: 1–500.");
        var allowed = EditorSettings.Defaults().Select(x => x.Action).ToHashSet();
        if (value.Shortcuts is null || value.Shortcuts.Any(x => !allowed.Contains(x.Action)) ||
            value.Shortcuts.Select(x => x.Action).Distinct().Count() != value.Shortcuts.Count)
            throw new ArgumentException("Неизвестная или повторяющаяся команда.");
        var gestures = value.Shortcuts.Where(x => x.Gesture.Length > 0)
            .Select(x => (System.Windows.Input.KeyGesture)new System.Windows.Input.KeyGestureConverter().ConvertFromString(x.Gesture)!).ToList();
        if (gestures.Select(x => (x.Key, x.Modifiers)).Distinct().Count() != gestures.Count) throw new ArgumentException("Горячие клавиши повторяются.");
        return value;
    }
    public static void Save(EditorSettings value)
    {
        Validate(value); Directory.CreateDirectory(WorkspaceStateService.StorageDirectory);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(PathName + ".tmp", PathName, true);
    }
}
public static class IssueLogService
{
    public static void Record(string message)
    {
        try { Directory.CreateDirectory(WorkspaceStateService.StorageDirectory); File.AppendAllText(Path.Combine(WorkspaceStateService.StorageDirectory, "problems.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
        catch { }
    }
}