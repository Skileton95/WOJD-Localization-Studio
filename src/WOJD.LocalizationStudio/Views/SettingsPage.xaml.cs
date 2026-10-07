using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class SettingsPage : UserControl
{
    public event EventHandler? SettingsSaved;

    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings();
        ShowPanel("General");
    }

    public void Reload()
    {
        EditorSettingsService.Reload();
        LoadSettings();
        SavedText.Text = string.Empty;
    }

    private void LoadSettings()
    {
        var settings = EditorSettingsService.Current;
        FontSizeBox.Text = settings.EditorFontSize.ToString("0.#", CultureInfo.InvariantCulture);
        WrapCheckBox.IsChecked = settings.WrapTranslation;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
            ShowPanel(id);
    }

    private void ShowPanel(string id)
    {
        GeneralPanel.Visibility = id == "General" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = id == "Files" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = id == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = id == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = id == "Backups" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(
                FontSizeBox.Text.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var fontSize))
        {
            AppDialog.Show(
                "Проверьте размер шрифта.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                Window.GetWindow(this));
            return;
        }

        var settings = EditorSettingsService.Current;
        settings.EditorFontSize = fontSize;
        settings.WrapTranslation = WrapCheckBox.IsChecked == true;
        EditorSettingsService.Save(settings);
        SavedText.Text = "Настройки сохранены";
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }
}
