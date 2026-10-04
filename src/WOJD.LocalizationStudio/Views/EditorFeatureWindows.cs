using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class StructureDiffWindow : Window
{
    public StructureDiffWindow(LocalizationEntry entry)
    {
        Title = $"Структура — {entry.Key}";
        Width = 980;
        Height = 620;
        MinWidth = 720;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var status = StructureProtectionService.Analyze(entry.Original, entry.Translation);
        var summary = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original)
                ? "— Исходный текст отсутствует — сравнение структуры невозможно."
                : status.CompactSummary,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(summary);

        var compare = new Grid();
        compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddPane(compare, 0, "Оригинал", entry.OriginalDisplay);
        AddPane(compare, 2, "Перевод", entry.Translation);
        Grid.SetRow(compare, 1);
        root.Children.Add(compare);

        var details = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original)
                ? "Без Original невозможно определить потерянные или лишние технические элементы."
                : status.StructuralResult.HasIssues || status.HasNewLineIssues
                    ? BuildDetails(status)
                    : "✓ Структура полностью совпадает с оригиналом.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
        };
        Grid.SetRow(details, 2);
        root.Children.Add(details);

        Content = root;
    }

    private static string BuildDetails(StructureProtectionStatus status)
    {
        var parts = new List<string>();
        if (status.StructuralResult.HasIssues)
            parts.Add(status.StructuralResult.Summary);
        if (status.HasNewLineIssues)
            parts.Add($"Переносы — оригинал: {status.SourceNewLineCount}, перевод: {status.TargetNewLineCount}");
        return string.Join("\n", parts);
    }

    private static void AddPane(Grid grid, int column, string title, string text)
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        };

        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13.5
        };

        foreach (var segment in TokenHighlightService.Parse(text))
        {
            var run = new Run(segment.Text);
            switch (segment.Kind)
            {
                case HighlightTokenKind.Tag:
                    run.Foreground = new SolidColorBrush(Color.FromRgb(109, 40, 217));
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case HighlightTokenKind.Placeholder:
                    run.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case HighlightTokenKind.Escape:
                    run.Foreground = new SolidColorBrush(Color.FromRgb(8, 145, 178));
                    run.FontWeight = FontWeights.SemiBold;
                    break;
            }
            block.Inlines.Add(run);
        }

        viewer.Content = block;
        Grid.SetRow(viewer, 1);
        panel.Children.Add(viewer);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }
}

public sealed class TranslationMemoryWindow : Window
{
    private readonly LocalizationDocument _document;
    private readonly LocalizationEntry _entry;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;

    public string? AppliedTranslation { get; private set; }

    public TranslationMemoryWindow(LocalizationDocument document, LocalizationEntry entry)
    {
        _document = document;
        _entry = entry;
        Title = "Translation Memory";
        Width = 980;
        Height = 590;
        MinWidth = 760;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _status = new TextBlock
        {
            Text = "Поиск похожих переводов…",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        root.Children.Add(_status);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "%", Binding = new Binding("Percent"), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Оригинал", Binding = new Binding("Original"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Перевод", Binding = new Binding("Translation"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.MouseDoubleClick += (_, _) => Apply();
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var apply = new Button { Content = "Применить", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        apply.Click += (_, _) => Apply();
        buttons.Children.Add(apply);
        var close = new Button { Content = "Закрыть", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += async (_, _) => await LoadSuggestionsAsync();
    }

    private async Task LoadSuggestionsAsync()
    {
        try
        {
            var items = await TranslationMemoryService.FindAsync(_document, _entry, 40);
            _grid.ItemsSource = items;
            _status.Text = items.Count == 0
                ? "Похожих переведённых строк не найдено."
                : $"Найдено предложений: {items.Count}. Двойной клик применяет выбранный перевод.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void Apply()
    {
        if (_grid.SelectedItem is not TranslationMemorySuggestion suggestion)
            return;
        AppliedTranslation = suggestion.Translation;
        DialogResult = true;
    }
}

public sealed class ConsistencyWindow : Window
{
    private readonly LocalizationDocument _document;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private IReadOnlyList<ConsistencyIssue> _issues = [];

    public LocalizationEntry? NavigateToEntry { get; private set; }

    public ConsistencyWindow(LocalizationDocument document)
    {
        _document = document;
        Title = "Проверка согласованности";
        Width = 1080;
        Height = 640;
        MinWidth = 780;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _status = new TextBlock
        {
            Text = "Анализ повторяющихся оригиналов…",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        root.Children.Add(_status);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Оригинал", Binding = new Binding("Original"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Вхождений", Binding = new Binding("TotalOccurrences"), Width = 110 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Вариантов", Binding = new Binding("VariantCount"), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Варианты перевода", Binding = new Binding("VariantPreview"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.MouseDoubleClick += (_, _) => Navigate();
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var navigate = new Button { Content = "Перейти к примеру", Padding = new Thickness(14, 8, 14, 8) };
        navigate.Click += (_, _) => Navigate();
        buttons.Children.Add(navigate);
        buttons.Children.Add(new Button { Content = "Закрыть", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += async (_, _) => await AnalyzeAsync();
    }

    private async Task AnalyzeAsync()
    {
        try
        {
            _issues = await Task.Run(() => ConsistencyService.Analyze(_document));
            _grid.ItemsSource = _issues.Select(x => new ConsistencyRow(x)).ToList();
            _status.Text = _issues.Count == 0
                ? "✓ Разных переводов для одинаковых оригиналов не найдено."
                : $"Найдено групп с разными переводами: {_issues.Count:N0}.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void Navigate()
    {
        if (_grid.SelectedItem is not ConsistencyRow row || row.Issue.Variants.Count == 0)
            return;
        NavigateToEntry = row.Issue.Variants[0].Example;
        DialogResult = true;
    }

    private sealed class ConsistencyRow
    {
        public ConsistencyRow(ConsistencyIssue issue)
        {
            Issue = issue;
            Original = issue.Original;
            TotalOccurrences = issue.TotalOccurrences;
            VariantCount = issue.VariantCount;
            VariantPreview = string.Join("  |  ", issue.Variants.Take(5).Select(x => $"{x.Translation} ×{x.Count}"));
        }
        public ConsistencyIssue Issue { get; }
        public string Original { get; }
        public int TotalOccurrences { get; }
        public int VariantCount { get; }
        public string VariantPreview { get; }
    }
}

public sealed class GlossaryWindow : Window
{
    private readonly ObservableCollection<GlossaryEntry> _items;
    private readonly DataGrid _grid;

    public GlossaryWindow()
    {
        Title = "Глоссарий";
        Width = 900;
        Height = 570;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = new ObservableCollection<GlossaryEntry>(
            GlossaryService.GetEntries().Select(Clone));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Обязательные термины участвуют в QA выбранных строк.",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
        });

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = _items,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Extended
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Оригинал", Binding = new Binding("Source") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Перевод", Binding = new Binding("Target") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Обязательный", Binding = new Binding("Required"), Width = 110 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Регистр", Binding = new Binding("CaseSensitive"), Width = 80 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Примечание", Binding = new Binding("Note") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var add = new Button { Content = "+ Термин", Padding = new Thickness(12, 8, 12, 8) };
        add.Click += (_, _) => _items.Add(new GlossaryEntry());
        buttons.Children.Add(add);
        var remove = new Button { Content = "Удалить", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) =>
        {
            foreach (var item in _grid.SelectedItems.OfType<GlossaryEntry>().ToList())
                _items.Remove(item);
        };
        buttons.Children.Add(remove);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(16, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            GlossaryService.Save(_items);
            DialogResult = true;
        };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Отмена", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private static GlossaryEntry Clone(GlossaryEntry x)
        => new()
        {
            Source = x.Source,
            Target = x.Target,
            Required = x.Required,
            CaseSensitive = x.CaseSensitive,
            Note = x.Note
        };
}

public sealed class ProjectHistoryWindow : Window
{
    public ProjectHistoryWindow(string filePath)
    {
        Title = "История проекта";
        Width = 980;
        Height = 580;
        MinWidth = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new DataGrid
        {
            Margin = new Thickness(16),
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = ProjectHistoryService.GetHistory(filePath)
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Время", Binding = new Binding("Time") { StringFormat = "dd.MM.yyyy HH:mm:ss" }, Width = 165 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Операция", Binding = new Binding("Operation"), Width = 190 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Строк", Binding = new Binding("AffectedEntries"), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Подробности", Binding = new Binding("Details"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Content = grid;
    }
}

public sealed class EditorSettingsWindow : Window
{
    private readonly TextBox _height = new();
    private readonly TextBox _fontSize = new();
    private readonly CheckBox _wrap = new() { Content = "Переносить длинный текст" };
    private readonly CheckBox _qa = new() { Content = "Показывать QA-панель" };
    private readonly CheckBox _original = new() { Content = "Показывать панель оригинала" };
    private readonly TextBox _aiConcurrency = new();
    private readonly TextBox _aiLimit = new();

    public EditorSettings Settings { get; private set; }

    public EditorSettingsWindow(EditorSettings settings)
    {
        Settings = Clone(settings);
        Title = "Настройки редактора";
        Width = 520;
        Height = 480;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var form = new StackPanel();
        AddField(form, "Высота редактора, px", _height);
        AddField(form, "Размер шрифта перевода", _fontSize);
        form.Children.Add(_wrap);
        form.Children.Add(_qa);
        form.Children.Add(_original);
        AddField(form, "ИИ: параллельных запросов (1–4)", _aiConcurrency);
        AddField(form, "ИИ: максимум строк за пакет (1–500)", _aiLimit);
        root.Children.Add(form);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var reset = new Button { Content = "По умолчанию", Padding = new Thickness(12, 8, 12, 8) };
        reset.Click += (_, _) => SetFields(new EditorSettings());
        buttons.Children.Add(reset);
        var save = new Button { Content = "Сохранить", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(12, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Отмена", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0), IsCancel = true });
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        Content = root;
        SetFields(Settings);
    }

    private static void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold });
        box.Padding = new Thickness(8, 6, 8, 6);
        panel.Children.Add(box);
    }

    private void SetFields(EditorSettings settings)
    {
        _height.Text = settings.EditorHeight.ToString("0", CultureInfo.InvariantCulture);
        _fontSize.Text = settings.TranslationFontSize.ToString("0.#", CultureInfo.InvariantCulture);
        _wrap.IsChecked = settings.WordWrap;
        _qa.IsChecked = settings.ShowQaPanel;
        _original.IsChecked = settings.ShowOriginalPane;
        _aiConcurrency.Text = settings.AiConcurrency.ToString(CultureInfo.InvariantCulture);
        _aiLimit.Text = settings.AiBatchLimit.ToString(CultureInfo.InvariantCulture);
    }

    private void Save()
    {
        if (!TryDouble(_height.Text, out var height) ||
            !TryDouble(_fontSize.Text, out var font) ||
            !int.TryParse(_aiConcurrency.Text, out var concurrency) ||
            !int.TryParse(_aiLimit.Text, out var limit))
        {
            MessageBox.Show(this, "Проверьте числовые значения.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings.EditorHeight = height;
        Settings.TranslationFontSize = font;
        Settings.WordWrap = _wrap.IsChecked == true;
        Settings.ShowQaPanel = _qa.IsChecked == true;
        Settings.ShowOriginalPane = _original.IsChecked == true;
        Settings.AiConcurrency = concurrency;
        Settings.AiBatchLimit = limit;
        EditorSettingsService.Save(Settings);
        Settings = EditorSettingsService.Load();
        DialogResult = true;
    }

    private static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static EditorSettings Clone(EditorSettings value)
        => new()
        {
            EditorHeight = value.EditorHeight,
            EditorCollapsed = value.EditorCollapsed,
            TranslationFontSize = value.TranslationFontSize,
            WordWrap = value.WordWrap,
            ShowQaPanel = value.ShowQaPanel,
            ShowOriginalPane = value.ShowOriginalPane,
            NamespaceColumnWidth = value.NamespaceColumnWidth,
            KeyColumnWidth = value.KeyColumnWidth,
            StatusColumnWidth = value.StatusColumnWidth,
            AiConcurrency = value.AiConcurrency,
            AiBatchLimit = value.AiBatchLimit
        };
}
