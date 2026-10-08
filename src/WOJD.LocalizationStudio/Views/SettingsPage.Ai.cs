using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class SettingsPage
{
    private StackPanel? _aiSettingsPanel;
    private TextBox? _aiModelBox;
    private PasswordBox? _aiApiKeyBox;
    private TextBox? _aiConcurrencyBox;
    private TextBox? _aiRetryBox;
    private TextBox? _aiBatchLimitBox;
    private TextBox? _aiInputPriceBox;
    private TextBox? _aiOutputPriceBox;
    private TextBlock? _aiStatusText;
    private bool _aiSettingsInstalled;

    [ModuleInitializer]
    internal static void RegisterAiSettingsLoadedHandler()
        => EventManager.RegisterClassHandler(
            typeof(SettingsPage),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ((SettingsPage)sender).InstallAiSettings()));

    private void InstallAiSettings()
    {
        if (_aiSettingsInstalled || Content is not Grid root)
            return;

        var contentGrid = GeneralPanel.Parent as Grid;
        var sidebar = root.Children
            .OfType<Border>()
            .FirstOrDefault(x => Grid.GetColumn(x) == 0)?.Child as StackPanel;
        if (contentGrid is null || sidebar is null)
            return;

        _aiSettingsPanel = BuildAiSettingsPanel();
        _aiSettingsPanel.Visibility = Visibility.Collapsed;
        contentGrid.Children.Add(_aiSettingsPanel);

        var aiButton = new Button
        {
            Content = "✦  ИИ",
            Tag = "__AI__",
            Style = TryFindResource("CategoryButton") as Style
        };
        aiButton.Click += (_, _) =>
        {
            ShowPanel("__AI__");
            if (_aiSettingsPanel is not null)
                _aiSettingsPanel.Visibility = Visibility.Visible;
            LoadAiSettingsValues();
        };
        sidebar.Children.Add(aiButton);

        foreach (var button in sidebar.Children.OfType<Button>().Where(x => !ReferenceEquals(x, aiButton)))
            button.Click += (_, _) =>
            {
                if (_aiSettingsPanel is not null)
                    _aiSettingsPanel.Visibility = Visibility.Collapsed;
            };

        AiUiWorkflow.ApplyStoredModel();
        LoadAiSettingsValues();
        _aiSettingsInstalled = true;
    }

    private StackPanel BuildAiSettingsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "ИИ",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "ИИ используется для проверки и исправления выбранных QA-проблем. Перед применением предложения всегда показывается предпросмотр.",
            Foreground = new SolidColorBrush(Color.FromRgb(95, 111, 137)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        });

        var apiCard = CreateAiCard("Подключение");
        var apiStack = (StackPanel)apiCard.Child;
        _aiApiKeyBox = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 7, 0, 0) };
        apiStack.Children.Add(new TextBlock { Text = "OpenAI API key" });
        apiStack.Children.Add(_aiApiKeyBox);
        apiStack.Children.Add(new TextBlock
        {
            Text = "Ключ действует только в текущем запуске и не записывается в настройки. Также поддерживается OPENAI_API_KEY.",
            Foreground = Brushes.Gray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        _aiStatusText = new TextBlock { Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };
        apiStack.Children.Add(_aiStatusText);
        panel.Children.Add(apiCard);

        var modelCard = CreateAiCard("Модель и пакетная обработка");
        var modelStack = (StackPanel)modelCard.Child;
        _aiModelBox = AddAiField(modelStack, "Модель");
        _aiConcurrencyBox = AddAiField(modelStack, "Параллельных запросов (1–6)");
        _aiRetryBox = AddAiField(modelStack, "Повторов при ошибке (0–5)");
        _aiBatchLimitBox = AddAiField(modelStack, "Максимум строк за один пакет (1–500)");
        panel.Children.Add(modelCard);

        var costCard = CreateAiCard("Оценка стоимости · необязательно");
        var costStack = (StackPanel)costCard.Child;
        _aiInputPriceBox = AddAiField(costStack, "USD за 1 млн входных токенов");
        _aiOutputPriceBox = AddAiField(costStack, "USD за 1 млн выходных токенов");
        costStack.Children.Add(new TextBlock
        {
            Text = "Оставьте 0, если не хотите показывать денежную оценку перед пакетной ИИ-проверкой.",
            Foreground = Brushes.Gray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        panel.Children.Add(costCard);

        var save = new Button
        {
            Content = "Сохранить настройки ИИ",
            Style = TryFindResource("PrimaryButton") as Style,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0)
        };
        save.Click += SaveAiSettings_Click;
        panel.Children.Add(save);
        return panel;
    }

    private static Border CreateAiCard(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(227, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 10, 0, 0),
            Child = stack
        };
    }

    private static TextBox AddAiField(StackPanel panel, string label)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) });
        var box = new TextBox { Padding = new Thickness(8, 6, 8, 6), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(box);
        return box;
    }

    private void LoadAiSettingsValues()
    {
        if (_aiModelBox is null)
            return;

        var settings = EditorSettingsService.Current;
        _aiModelBox.Text = settings.AiModel;
        _aiConcurrencyBox!.Text = settings.AiConcurrency.ToString(CultureInfo.InvariantCulture);
        _aiRetryBox!.Text = settings.AiRetryCount.ToString(CultureInfo.InvariantCulture);
        _aiBatchLimitBox!.Text = settings.AiBatchLimit.ToString(CultureInfo.InvariantCulture);
        _aiInputPriceBox!.Text = settings.AiInputUsdPerMillionTokens.ToString(CultureInfo.InvariantCulture);
        _aiOutputPriceBox!.Text = settings.AiOutputUsdPerMillionTokens.ToString(CultureInfo.InvariantCulture);
        _aiStatusText!.Text = AiCorrectionService.IsConfigured
            ? "API key доступен для текущего запуска."
            : "API key пока не задан.";
        _aiStatusText.Foreground = AiCorrectionService.IsConfigured
            ? new SolidColorBrush(Color.FromRgb(40, 147, 91))
            : new SolidColorBrush(Color.FromRgb(180, 112, 30));
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_aiModelBox is null || string.IsNullOrWhiteSpace(_aiModelBox.Text) ||
            !int.TryParse(_aiConcurrencyBox?.Text, out var concurrency) || concurrency is < 1 or > 6 ||
            !int.TryParse(_aiRetryBox?.Text, out var retries) || retries is < 0 or > 5 ||
            !int.TryParse(_aiBatchLimitBox?.Text, out var batchLimit) || batchLimit is < 1 or > 500 ||
            !TryParseAiDecimal(_aiInputPriceBox?.Text, out var inputPrice) || inputPrice < 0 ||
            !TryParseAiDecimal(_aiOutputPriceBox?.Text, out var outputPrice) || outputPrice < 0)
        {
            AppDialog.Show(
                "Проверьте модель и числовые параметры ИИ.",
                "Настройки ИИ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                Window.GetWindow(this));
            return;
        }

        var settings = EditorSettingsService.Current;
        settings.AiModel = _aiModelBox.Text.Trim();
        settings.AiConcurrency = concurrency;
        settings.AiRetryCount = retries;
        settings.AiBatchLimit = batchLimit;
        settings.AiInputUsdPerMillionTokens = inputPrice;
        settings.AiOutputUsdPerMillionTokens = outputPrice;
        EditorSettingsService.Save(settings);

        AiCorrectionService.Model = settings.AiModel;
        if (!string.IsNullOrWhiteSpace(_aiApiKeyBox?.Password))
            AiCorrectionService.ConfigureSession(_aiApiKeyBox.Password, settings.AiModel);

        if (_aiApiKeyBox is not null)
            _aiApiKeyBox.Password = string.Empty;
        LoadAiSettingsValues();
        SavedText.Text = "Настройки ИИ сохранены";
    }

    private static bool TryParseAiDecimal(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}
