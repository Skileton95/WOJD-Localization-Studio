using System.IO;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class EditorSettings
{
    public double EditorHeight { get; set; } = 360;
    public bool EditorCollapsed { get; set; }
    public double EditorFontSize { get; set; } = 14;
    public bool WrapTranslation { get; set; } = true;
    public bool ShowTokenPreview { get; set; } = true;
    public bool ShowQaDetails { get; set; } = true;
    public bool ShowNamespaceColumn { get; set; } = true;
    public bool ShowOriginalColumn { get; set; } = true;
    public bool ShowStatusColumn { get; set; } = true;
    public int AiConcurrency { get; set; } = 2;
    public int AiRetryCount { get; set; } = 2;
    public int AiBatchLimit { get; set; } = 100;
    public decimal AiInputUsdPerMillionTokens { get; set; }
    public decimal AiOutputUsdPerMillionTokens { get; set; }
}

public static class EditorSettingsService
{
    private static readonly object Sync = new();
    private static EditorSettings? _current;

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
                return _current ??= LoadCore();
        }
    }

    public static void Save(EditorSettings settings)
    {
        lock (Sync)
        {
            Normalize(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temp = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, SettingsPath, true);
            _current = settings;
        }
    }

    public static EditorSettings Reload()
    {
        lock (Sync)
            return _current = LoadCore();
    }

    private static EditorSettings LoadCore()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<EditorSettings>(
                    File.ReadAllText(SettingsPath, Encoding.UTF8),
                    JsonOptions) ?? new EditorSettings();
                Normalize(settings);
                return settings;
            }
        }
        catch
        {
        }

        return new EditorSettings();
    }

    private static void Normalize(EditorSettings settings)
    {
        settings.EditorHeight = Math.Clamp(settings.EditorHeight, 180, 760);
        settings.EditorFontSize = Math.Clamp(settings.EditorFontSize, 10, 28);
        settings.AiConcurrency = Math.Clamp(settings.AiConcurrency, 1, 6);
        settings.AiRetryCount = Math.Clamp(settings.AiRetryCount, 0, 5);
        settings.AiBatchLimit = Math.Clamp(settings.AiBatchLimit, 1, 500);
        settings.AiInputUsdPerMillionTokens = Math.Max(0, settings.AiInputUsdPerMillionTokens);
        settings.AiOutputUsdPerMillionTokens = Math.Max(0, settings.AiOutputUsdPerMillionTokens);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
