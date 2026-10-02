using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class SettingsWindow : Window
{
    private AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();

        _settings =
            Clone(
                AppSettingsService.Current);

        LoadControls();
    }

    public AppSettings Settings => _settings;

    private void LoadControls()
    {
        ThemeBox.SelectedIndex =
            string.Equals(
                _settings.Theme,
                "Dark",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;

        FontSizeBox.Text =
            _settings.FontSize.ToString(
                CultureInfo.InvariantCulture);

        FilesWidthBox.Text =
            _settings.FilesPanelWidth.ToString(
                CultureInfo.InvariantCulture);

        BackupLimitBox.Text =
            _settings.BackupLimit.ToString();

        ProjectSearchHotkeyBox.Text =
            GetHotkey("ProjectSearch", "Ctrl+Shift+F");

        NextHotkeyBox.Text =
            GetHotkey("NextUntranslated", "F6");

        PreviousHotkeyBox.Text =
            GetHotkey("PreviousUntranslated", "Shift+F6");

        SaveAllHotkeyBox.Text =
            GetHotkey("SaveAll", "Ctrl+Shift+S");
    }

    private string GetHotkey(
        string key,
        string fallback)
        => _settings.Hotkeys.TryGetValue(
               key,
               out var value)
           ? value
           : fallback;

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!double.TryParse(
                FontSizeBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var fontSize) ||
            fontSize < 9 ||
            fontSize > 28)
        {
            AppDialog.Show(
                "Размер шрифта должен быть от 9 до 28.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);

            return;
        }

        if (!double.TryParse(
                FilesWidthBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var filesWidth) ||
            filesWidth < 180 ||
            filesWidth > 700)
        {
            AppDialog.Show(
                "Ширина панели файлов должна быть от 180 до 700.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);

            return;
        }

        if (!int.TryParse(
                BackupLimitBox.Text,
                out var backupLimit) ||
            backupLimit < 1 ||
            backupLimit > 500)
        {
            AppDialog.Show(
                "Количество backup должно быть от 1 до 500.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);

            return;
        }

        _settings.Theme =
            (ThemeBox.SelectedItem as ComboBoxItem)
                ?.Content?.ToString()
            ?? "Light";

        _settings.FontSize =
            fontSize;

        _settings.FilesPanelWidth =
            filesWidth;

        _settings.BackupLimit =
            backupLimit;

        _settings.Hotkeys["ProjectSearch"] =
            ProjectSearchHotkeyBox.Text.Trim();

        _settings.Hotkeys["NextUntranslated"] =
            NextHotkeyBox.Text.Trim();

        _settings.Hotkeys["PreviousUntranslated"] =
            PreviousHotkeyBox.Text.Trim();

        _settings.Hotkeys["SaveAll"] =
            SaveAllHotkeyBox.Text.Trim();

        AppSettingsService.Save(
            _settings);

        DialogResult = true;
    }

    private void Reset_Click(
        object sender,
        RoutedEventArgs e)
    {
        _settings =
            new AppSettings();

        LoadControls();
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static AppSettings Clone(
        AppSettings source)
        => new()
        {
            Theme = source.Theme,
            FontSize = source.FontSize,
            FilesPanelWidth = source.FilesPanelWidth,
            FilesPanelCollapsed = source.FilesPanelCollapsed,
            WindowWidth = source.WindowWidth,
            WindowHeight = source.WindowHeight,
            WindowLeft = source.WindowLeft,
            WindowTop = source.WindowTop,
            WindowMaximized = source.WindowMaximized,
            BackupLimit = source.BackupLimit,
            Hotkeys =
                new Dictionary<string, string>(
                    source.Hotkeys,
                    StringComparer.OrdinalIgnoreCase)
        };
}
