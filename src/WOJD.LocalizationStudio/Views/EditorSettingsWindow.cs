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
    private readonly CheckBox _tokens;
    private readonly CheckBox _qaDetails;
    private readonly CheckBox _namespaceColumn;
    private readonly CheckBox _originalColumn;
    private readonly CheckBox _statusColumn;
    private readonly TextBox _aiConcurrency;
    private readonly TextBox _aiRetries;
    private readonly TextBox _aiLimit;
    private readonly TextBox _inputPrice;
    private readonly TextBox _outputPrice;

    public EditorSettingsWindow()
    {
        var settings = EditorSettingsService.Current;
        Title = "Настройки редактора";
        Width = 650;
        Height = 660;
        MinWidth = 560;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = root;

        root.Children.Add(Section("Интерфейс"));
        _height = AddText(root, "Высота редактора, px", settings.EditorHeight.ToString("0", CultureInfo.InvariantCulture));
        _fontSize = AddText(root, "Размер шрифта перевода", settings.EditorFontSize.ToString("0.#", CultureInfo.InvariantCulture));
        _wrap = AddCheck(root, "Переносить длинные строки", settings.WrapTranslation);
        _tokens = AddCheck(root, "Показывать цветной предпросмотр тегов и плейсхолдеров", settings.ShowTokenPreview);
        _qaDetails = AddCheck(root, "Показывать подробный структурный QA", settings.ShowQaDetails);

        root.Children.Add(Section("Колонки таблицы"));
        _namespaceColumn = AddCheck(root, "Namespace", settings.ShowNamespaceColumn);
        _originalColumn = AddCheck(root, "Оригинал", settings.ShowOriginalColumn);
        _statusColumn = AddCheck(root, "Статус", settings.ShowStatusColumn);

        root.Children.Add(Section("ИИ-проверка"));
        _aiConcurrency = AddText(root, "Параллельных запросов", settings.AiConcurrency.ToString(CultureInfo.InvariantCulture));
        _aiRetries = AddText(root, "Повторов при ошибке", settings.AiRetryCount.ToString(CultureInfo.InvariantCulture));
        _aiLimit = AddText(root, "Максимум строк за пакет", settings.AiBatchLimit.ToString(CultureInfo.InvariantCulture));
        _inputPrice = AddText(root, "Цена input за 1M токенов, USD (0 = не считать)", settings.AiInputUsdPerMillionTokens.ToString(CultureInfo.InvariantCulture));
        _outputPrice = AddText(root, "Цена output за 1M токенов, USD (0 = не считать)", settings.AiOutputUsdPerMillionTokens.ToString(CultureInfo.InvariantCulture));

        root.Children.Add(new TextBlock
        {
            Text = "Цены не зашиты в программу, потому что тариф зависит от выбранной модели. Укажите актуальные значения сами — тогда пакетная ИИ-проверка покажет ориентировочную стоимость до запуска.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 14)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var save = new Button
        {
            Content = "Сохранить",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        buttons.Children.Add(new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });
        root.Children.Add(buttons);
        Content = scroll;
    }

    private void Save()
    {
        if (!double.TryParse(_height.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            !double.TryParse(_fontSize.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize) ||
            !int.TryParse(_aiConcurrency.Text, out var concurrency) ||
            !int.TryParse(_aiRetries.Text, out var retries) ||
            !int.TryParse(_aiLimit.Text, out var limit) ||
            !decimal.TryParse(_inputPrice.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var inputPrice) ||
            !decimal.TryParse(_outputPrice.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var outputPrice))
        {
            MessageBox.Show(this, "Проверьте числовые значения.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = EditorSettingsService.Current;
        settings.EditorHeight = height;
        settings.EditorFontSize = fontSize;
        settings.WrapTranslation = _wrap.IsChecked == true;
        settings.ShowTokenPreview = _tokens.IsChecked == true;
        settings.ShowQaDetails = _qaDetails.IsChecked == true;
        settings.ShowNamespaceColumn = _namespaceColumn.IsChecked == true;
        settings.ShowOriginalColumn = _originalColumn.IsChecked == true;
        settings.ShowStatusColumn = _statusColumn.IsChecked == true;
        settings.AiConcurrency = concurrency;
        settings.AiRetryCount = retries;
        settings.AiBatchLimit = limit;
        settings.AiInputUsdPerMillionTokens = inputPrice;
        settings.AiOutputUsdPerMillionTokens = outputPrice;
        EditorSettingsService.Save(settings);
        DialogResult = true;
    }

    private static TextBlock Section(string text)
        => new()
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 8)
        };

    private static TextBox AddText(Panel root, string label, string value)
    {
        root.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 6, 0, 3)
        });
        var box = new TextBox
        {
            Text = value,
            Padding = new Thickness(8, 6, 8, 6),
            MaxWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(box);
        return box;
    }

    private static CheckBox AddCheck(Panel root, string text, bool value)
    {
        var check = new CheckBox
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(0, 5, 0, 3)
        };
        root.Children.Add(check);
        return check;
    }
}
