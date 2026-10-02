using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WOJD.LocalizationStudio.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public double FontSize { get; set; } = 14;
    public double FilesPanelWidth { get; set; } = 300;
    public bool FilesPanelCollapsed { get; set; }

    public double WindowWidth { get; set; } = 1540;
    public double WindowHeight { get; set; } = 980;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    public int BackupLimit { get; set; } = 30;

    public Dictionary<string, string> Hotkeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectSearch"] = "Ctrl+Shift+F",
            ["NextUntranslated"] = "F6",
            ["PreviousUntranslated"] = "Shift+F6",
            ["SaveAll"] = "Ctrl+Shift+S"
        };
}

public static class AppSettingsService
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "WOJD Localization Studio");

    private static readonly string SettingsPath =
        Path.Combine(
            SettingsDirectory,
            "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                Current =
                    JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(SettingsPath),
                        JsonOptions)
                    ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        ApplyVisualSettings();
        return Current;
    }

    public static void Save(AppSettings settings)
    {
        Current = settings;

        Directory.CreateDirectory(
            SettingsDirectory);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(
                settings,
                JsonOptions));

        ApplyVisualSettings();
    }

    public static void ApplyVisualSettings()
    {
        if (Application.Current is null)
            return;

        var dark =
            string.Equals(
                Current.Theme,
                "Dark",
                StringComparison.OrdinalIgnoreCase);

        SetColor(
            "BackgroundColor",
            dark ? "#161A22" : "#F7F9FC");

        SetColor(
            "SurfaceColor",
            dark ? "#202631" : "#FFFFFF");

        SetColor(
            "BorderColor",
            dark ? "#343D4B" : "#E3E8F0");

        SetColor(
            "TextColor",
            dark ? "#EEF3FB" : "#17233C");

        SetColor(
            "MutedTextColor",
            dark ? "#A7B1C2" : "#71809A");

        Application.Current.Resources[
            typeof(System.Windows.Controls.Control)] =
            Application.Current.Resources[
                typeof(System.Windows.Controls.Control)];

        foreach (Window window in
                 Application.Current.Windows)
        {
            window.FontSize =
                Current.FontSize;
        }
    }

    private static void SetColor(
        string key,
        string hex)
    {
        var color =
            (Color)ColorConverter.ConvertFromString(hex);

        Application.Current!.Resources[key] =
            color;

        var brushKey =
            key switch
            {
                "BackgroundColor" => "BackgroundBrush",
                "SurfaceColor" => "SurfaceBrush",
                "BorderColor" => "BorderBrushApp",
                "TextColor" => "TextBrush",
                "MutedTextColor" => "MutedTextBrush",
                _ => null
            };

        if (brushKey is not null)
        {
            Application.Current.Resources[brushKey] =
                new SolidColorBrush(color);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
}
