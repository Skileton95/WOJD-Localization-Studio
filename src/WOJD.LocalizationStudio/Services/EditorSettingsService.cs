using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class EditorSettings
{
    public double EditorHeight { get; set; } = 320;
    public bool EditorCollapsed { get; set; }
    public double TranslationFontSize { get; set; } = 14;
    public bool WordWrap { get; set; } = true;
    public bool ShowQaPanel { get; set; } = true;
    public bool ShowOriginalPane { get; set; } = true;
    public bool ShowNamespaceColumn { get; set; } = true;
    public bool ShowKeyColumn { get; set; } = true;
    public bool ShowStatusColumn { get; set; } = true;
    public bool CompactTable { get; set; }
    public double NamespaceColumnWidth { get; set; } = 250;
    public double KeyColumnWidth { get; set; } = 150;
    public double StatusColumnWidth { get; set; } = 150;
    public int AiConcurrency { get; set; } = 2;
    public int AiBatchLimit { get; set; } = 100;
}

public static class EditorSettingsService
{
    private static readonly object Sync = new();
    private static EditorSettings? _cached;

    public static string SettingsPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "editor-settings.json");

    public static EditorSettings Current
    {
        get
        {
            lock (Sync)
                return _cached ??= LoadCore();
        }
    }

    public static EditorSettings Load()
    {
        lock (Sync)
            return _cached = LoadCore();
    }

    public static void Save(EditorSettings settings)
    {
        lock (Sync)
        {
            // EditorSettingsWindow in this release edits the text/editor options;
            // table view toggles are changed from the DataGrid context menu.
            // Preserve them when the dialog saves a cloned settings object.
            if (_cached is not null && !ReferenceEquals(settings, _cached))
            {
                settings.ShowNamespaceColumn = _cached.ShowNamespaceColumn;
                settings.ShowKeyColumn = _cached.ShowKeyColumn;
                settings.ShowStatusColumn = _cached.ShowStatusColumn;
                settings.CompactTable = _cached.CompactTable;
            }

            settings = Normalize(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temp = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, SettingsPath, true);
            _cached = settings;
        }
    }

    public static void Reset()
        => Save(new EditorSettings());

    private static EditorSettings LoadCore()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new EditorSettings();

            var value = JsonSerializer.Deserialize<EditorSettings>(
                File.ReadAllText(SettingsPath, Encoding.UTF8),
                JsonOptions);

            return Normalize(value ?? new EditorSettings());
        }
        catch
        {
            return new EditorSettings();
        }
    }

    private static EditorSettings Normalize(EditorSettings value)
    {
        value.EditorHeight = Math.Clamp(value.EditorHeight, 180, 700);
        value.TranslationFontSize = Math.Clamp(value.TranslationFontSize, 10, 28);
        value.NamespaceColumnWidth = Math.Clamp(value.NamespaceColumnWidth, 100, 600);
        value.KeyColumnWidth = Math.Clamp(value.KeyColumnWidth, 80, 500);
        value.StatusColumnWidth = Math.Clamp(value.StatusColumnWidth, 90, 320);
        value.AiConcurrency = Math.Clamp(value.AiConcurrency, 1, 4);
        value.AiBatchLimit = Math.Clamp(value.AiBatchLimit, 1, 500);
        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
}
