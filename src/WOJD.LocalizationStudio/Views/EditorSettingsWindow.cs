using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class EditorSettingsWindow : Window
{
    private readonly TextBox _height;
    private readonly TextBox _fontSize;
    private readonly CheckBox _wrap;
    private readonly CheckBox _showQa;
    private readonly CheckBox _rememberColumns;
    private readonly CheckBox _showTm;
    private readonly TextBox _tmLimit;
    private readonly TextBox _tmThreshold;
    private readonly TextBox _aiConcurrency;
    private readonly TextBox _aiBatch;
    private readonly CheckBox _watchExternal;

    public EditorSettingsWindow()
    {
        var value = EditorSettingsService.Snapshot();

        Title = "Настройки редактора";
        Width = 560;
        Height = 620;
        MinWidth = 520;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
        root.Children.Add(scroll);

        panel.Children.Add(Heading("Редактор"));
        _height = AddNumber(panel, "Высота нижнего редактора (px)", value.EditorHeight.ToString("0", CultureInfo.InvariantCulture));
        _fontSize = AddNumber(panel, "Размер шрифта перевода", value.TranslationFontSize.ToString("0.#", CultureInfo.InvariantCulture));
        _wrap = AddCheck(panel, "Переносить длинные строки", value.WrapTranslation);
        _showQa = AddCheck(panel, "Показывать компактную QA-панель", value.ShowQaPanel);
        _rememberColumns = AddCheck(panel, "Запоминать ширину столбцов", value.RememberColumnWidths);

        panel.Children.Add(Heading("Translation Memory"));
        _showTm = AddCheck(panel, "Показывать Translation Memory", value.ShowTranslationMemory);
        _tmLimit = AddNumber(panel, "Количество предложений", value.TranslationMemoryLimit.ToString(CultureInfo.InvariantCulture));
        _tmThreshold = AddNumber(panel, "Минимальное сходство (0.1–1.0)", value.TranslationMemoryThreshold.ToString("0.00", CultureInfo.InvariantCulture));

        panel.Children.Add(Heading("ИИ"));
        _aiConcurrency = AddNumber(panel, "Одновременных запросов", value.AiConcurrency.ToString(CultureInfo.InvariantCulture));
        _aiBatch = AddNumber(panel, "Максимум строк за пакет", value.AiBatchLimit.ToString(CultureInfo.InvariantCulture));

        panel.Children.Add(Heading("Файлы"));
        _watchExternal = AddCheck(panel, "Следить за внешними изменениями открытого файла", value.WatchExternalChanges);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        buttons.Children.Add(cancel);

        var save = new Button
        {
            Content = "Сохранить",
            Padding = new Thickness(16, 8, 16, 8),
            IsDefault = true
        };
        save.Click += (_, _) => SaveSettings();
        buttons.Children.Add(save);

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
    }

    private void SaveSettings()
    {
        if (!TryDouble(_height, 180, 700, out var height, "Высота редактора") ||
            !TryDouble(_fontSize, 10, 28, out var font, "Размер шрифта") ||
            !TryInt(_tmLimit, 1, 50, out var tmLimit, "Количество TM-предложений") ||
            !TryDouble(_tmThreshold, 0.1, 1.0, out var tmThreshold, "Порог TM") ||
            !TryInt(_aiConcurrency, 1, 8, out var concurrency, "Concurrency ИИ") ||
            !TryInt(_aiBatch, 1, 500, out var batch, "Размер пакета ИИ"))
        {
            return;
        }

        var value = EditorSettingsService.Snapshot();
        value.EditorHeight = height;
        value.TranslationFontSize = font;
        value.WrapTranslation = _wrap.IsChecked == true;
        value.ShowQaPanel = _showQa.IsChecked == true;
        value.RememberColumnWidths = _rememberColumns.IsChecked == true;
        value.ShowTranslationMemory = _showTm.IsChecked == true;
        value.TranslationMemoryLimit = tmLimit;
        value.TranslationMemoryThreshold = tmThreshold;
        value.AiConcurrency = concurrency;
        value.AiBatchLimit = batch;
        value.WatchExternalChanges = _watchExternal.IsChecked == true;
        EditorSettingsService.Save(value);
        DialogResult = true;
    }

    private bool TryDouble(TextBox box, double min, double max, out double value, string name)
    {
        var text = box.Text.Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
            return true;

        MessageBox.Show(this, $"{name}: допустимо от {min} до {max}.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        return false;
    }

    private bool TryInt(TextBox box, int min, int max, out int value, string name)
    {
        if (int.TryParse(box.Text, out value) && value >= min && value <= max)
            return true;

        MessageBox.Show(this, $"{name}: допустимо от {min} до {max}.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        return false;
    }

    private static TextBlock Heading(string text)
        => new()
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 8)
        };

    private static TextBox AddNumber(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 4) });
        var box = new TextBox { Text = value, Padding = new Thickness(8, 6, 8, 6), Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(box);
        return box;
    }

    private static CheckBox AddCheck(Panel panel, string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 7, 0, 3) };
        panel.Children.Add(box);
        return box;
    }
}
