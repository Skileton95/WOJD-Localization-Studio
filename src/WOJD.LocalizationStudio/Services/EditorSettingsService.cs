using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class EditorSettings
{
    public double EditorHeight { get; set; } = 280;
    public double TranslationFontSize { get; set; } = 13;
    public bool WrapTranslation { get; set; } = true;
    public bool ShowQaPanel { get; set; } = true;
    public bool RememberColumnWidths { get; set; } = true;
    public bool ShowTranslationMemory { get; set; } = true;
    public int TranslationMemoryLimit { get; set; } = 8;
    public double TranslationMemoryThreshold { get; set; } = 0.58;
    public int AiConcurrency { get; set; } = 2;
    public int AiBatchLimit { get; set; } = 100;
    public bool WatchExternalChanges { get; set; } = true;
    public Dictionary<string, double> ColumnWidths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class EditorSettingsService
{
    private static readonly object Sync = new();
    private static EditorSettings? _cached;

    public static event EventHandler? Changed;

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

    public static EditorSettings Snapshot()
    {
        lock (Sync)
        {
            var value = Current;
            return new EditorSettings
            {
                EditorHeight = value.EditorHeight,
                TranslationFontSize = value.TranslationFontSize,
                WrapTranslation = value.WrapTranslation,
                ShowQaPanel = value.ShowQaPanel,
                RememberColumnWidths = value.RememberColumnWidths,
                ShowTranslationMemory = value.ShowTranslationMemory,
                TranslationMemoryLimit = value.TranslationMemoryLimit,
                TranslationMemoryThreshold = value.TranslationMemoryThreshold,
                AiConcurrency = value.AiConcurrency,
                AiBatchLimit = value.AiBatchLimit,
                WatchExternalChanges = value.WatchExternalChanges,
                ColumnWidths = new Dictionary<string, double>(value.ColumnWidths, StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    public static void Save(EditorSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        value.EditorHeight = Math.Clamp(value.EditorHeight, 180, 700);
        value.TranslationFontSize = Math.Clamp(value.TranslationFontSize, 10, 28);
        value.TranslationMemoryLimit = Math.Clamp(value.TranslationMemoryLimit, 1, 50);
        value.TranslationMemoryThreshold = Math.Clamp(value.TranslationMemoryThreshold, 0.1, 1.0);
        value.AiConcurrency = Math.Clamp(value.AiConcurrency, 1, 8);
        value.AiBatchLimit = Math.Clamp(value.AiBatchLimit, 1, 500);

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(value, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temp, SettingsPath, true);
            _cached = value;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Update(Action<EditorSettings> update)
    {
        var value = Snapshot();
        update(value);
        Save(value);
    }

    private static EditorSettings LoadCore()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new EditorSettings();

            var value = JsonSerializer.Deserialize<EditorSettings>(
                File.ReadAllText(SettingsPath, Encoding.UTF8),
                JsonOptions) ?? new EditorSettings();

            value.ColumnWidths ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            return value;
        }
        catch
        {
            return new EditorSettings();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
