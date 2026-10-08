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
        InstallAiSettings();
        LoadSettings();
        ShowPanel("General");
    }

    public void Reload()
    {
        EditorSettingsService.Reload();
        LoadSettings();
        LoadAiSettingsValues();
        SavedText.Text = string.Empty;
    }

    private void LoadSettings()
    {
        var settings = EditorSettingsService.Current;
        FontSizeBox.Text = settings.EditorFontSize.ToString("0.#", CultureInfo.InvariantCulture);
        WrapCheckBox.IsChecked = settings.WrapTranslation;
        QaDetailsCheckBox.IsChecked = settings.ShowQaDetails;
        ShowContextCheckBox.IsChecked = settings.ShowContextPane;
        CompactListCheckBox.IsChecked = settings.CompactEntryList;
        ListWidthBox.Text = settings.TranslationListWidth.ToString("0", CultureInfo.InvariantCulture);
        ContextWidthBox.Text = settings.ContextPaneWidth.ToString("0", CultureInfo.InvariantCulture);
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
        if (!TryReadNumber(FontSizeBox.Text, 10, 28, "размер шрифта", out var fontSize) ||
            !TryReadNumber(ListWidthBox.Text, 250, 700, "ширину списка строк", out var listWidth) ||
            !TryReadNumber(ContextWidthBox.Text, 240, 620, "ширину панели контекста", out var contextWidth))
        {
            return;
        }

        var settings = EditorSettingsService.Current;
        settings.EditorFontSize = fontSize;
        settings.WrapTranslation = WrapCheckBox.IsChecked == true;
        settings.ShowQaDetails = QaDetailsCheckBox.IsChecked == true;
        settings.ShowContextPane = ShowContextCheckBox.IsChecked == true;
        settings.CompactEntryList = CompactListCheckBox.IsChecked == true;
        settings.TranslationListWidth = listWidth;
        settings.ContextPaneWidth = contextWidth;
        EditorSettingsService.Save(settings);
        SavedText.Text = "Настройки сохранены";

        if (Window.GetWindow(this) is ShellWindow shell)
            shell.ApplyAppearanceSettings();

        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private bool TryReadNumber(
        string text,
        double min,
        double max,
        string label,
        out double value)
    {
        if (double.TryParse(
                text.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) &&
            value >= min &&
            value <= max)
        {
            return true;
        }

        AppDialog.Show(
            $"Проверьте {label}. Допустимый диапазон: {min:0}–{max:0}.",
            "Настройки",
            MessageBoxButton.OK,
            MessageBoxImage.Warning,
            Window.GetWindow(this));
        return false;
    }
}
