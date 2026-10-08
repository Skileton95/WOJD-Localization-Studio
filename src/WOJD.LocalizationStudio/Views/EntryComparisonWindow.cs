using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class EntryComparisonWindow : Window
{
    private readonly LocalizationEntry _leftEntry;
    private readonly EntryRelationIndex? _relationIndex;
    private readonly List<ComparisonOption> _options;
    private readonly StackPanel _body = new();
    private readonly TextBlock _rightKeyText = new();
    private readonly TextBlock _rightMetaText = new();
    private readonly TextBlock _relationText = new();
    private ComboBox? _selector;

    public EntryComparisonWindow(LocalizationEntry leftEntry, LocalizationEntry rightEntry)
        : this(leftEntry, rightEntry, [rightEntry], null)
    {
    }

    public EntryComparisonWindow(
        LocalizationEntry leftEntry,
        LocalizationEntry rightEntry,
        IEnumerable<LocalizationEntry> candidates,
        EntryRelationIndex? relationIndex)
    {
        _leftEntry = leftEntry;
        _relationIndex = relationIndex;

        var rows = candidates
            .Where(x => !ReferenceEquals(x, leftEntry))
            .Distinct()
            .ToList();
        if (!rows.Contains(rightEntry))
            rows.Insert(0, rightEntry);

        _options = rows
            .Select(x => new ComparisonOption(x, BuildOptionLabel(x, relationIndex)))
            .ToList();

        Title = "Сравнение строк A / B";
        Width = 1060;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
        Content = BuildContent(rightEntry);
    }

    private UIElement BuildContent(LocalizationEntry initialRight)
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(CreateLeftHeader(_leftEntry));
        header.Children.Add(CreateRightHeader(initialRight));
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        SelectRight(initialRight);
        RefreshComparison(initialRight);
        return root;
    }

    private UIElement CreateLeftHeader(LocalizationEntry entry)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Строка A · текущая",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(47, 112, 245))
        });
        panel.Children.Add(new TextBlock
        {
            Text = entry.Key,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = BuildMeta(entry),
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(panel, 0);
        return panel;
    }

    private UIElement CreateRightHeader(LocalizationEntry initialRight)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Строка B · выберите связанную строку",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154))
        });

        _selector = new ComboBox
        {
            ItemsSource = _options,
            DisplayMemberPath = nameof(ComparisonOption.Label),
            Margin = new Thickness(0, 5, 0, 7),
            MinHeight = 34,
            MaxDropDownHeight = 360
        };
        _selector.SelectionChanged += (_, _) =>
        {
            if (_selector.SelectedItem is ComparisonOption option)
                RefreshComparison(option.Entry);
        };
        panel.Children.Add(_selector);

        _rightKeyText.FontSize = 16;
        _rightKeyText.FontWeight = FontWeights.SemiBold;
        _rightKeyText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_rightKeyText);

        _rightMetaText.Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154));
        _rightMetaText.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_rightMetaText);

        _relationText.Foreground = new SolidColorBrush(Color.FromRgb(47, 112, 245));
        _relationText.Margin = new Thickness(0, 4, 0, 0);
        _relationText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_relationText);

        Grid.SetColumn(panel, 2);
        return panel;
    }

    private void SelectRight(LocalizationEntry entry)
    {
        if (_selector is null)
            return;

        _selector.SelectedItem = _options.FirstOrDefault(x => ReferenceEquals(x.Entry, entry))
                                 ?? _options.FirstOrDefault();
    }

    private void RefreshComparison(LocalizationEntry rightEntry)
    {
        _rightKeyText.Text = rightEntry.Key;
        _rightMetaText.Text = BuildMeta(rightEntry);
        _relationText.Text = _relationIndex is null
            ? string.Empty
            : "Связь: " + _relationIndex.DescribeRelation(_leftEntry, rightEntry);

        _body.Children.Clear();
        _body.Children.Add(CreateComparisonSection(
            "Original A / Original B",
            _leftEntry.OriginalDisplay,
            rightEntry.OriginalDisplay));
        _body.Children.Add(CreateComparisonSection(
            "Translation A / Translation B",
            _leftEntry.Translation,
            rightEntry.Translation));
    }

    private static string BuildOptionLabel(
        LocalizationEntry entry,
        EntryRelationIndex? index)
    {
        var family = index?.GetFamily(entry) ?? EntryRelationIndex.GetKeyFamily(entry.Key);
        var role = EntryRelationIndex.GetSemanticRole(entry.Key, family);
        return string.IsNullOrWhiteSpace(family)
            ? $"{role} · {entry.Key}"
            : $"{family}  ›  {role}";
    }

    private static string BuildMeta(LocalizationEntry entry)
        => string.IsNullOrWhiteSpace(entry.Namespace)
            ? $"ID: {entry.Index:N0}"
            : $"{entry.Namespace}  ·  ID: {entry.Index:N0}";

    private static UIElement CreateComparisonSection(string title, string left, string right)
    {
        var diff = TextDiffService.Compare(left, right);
        var section = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 242)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headingGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headingGrid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        });
        var changeLabel = new TextBlock
        {
            Text = diff.ChangedSegments == 0 ? "совпадает" : "изменения выделены",
            Foreground = diff.ChangedSegments == 0
                ? new SolidColorBrush(Color.FromRgb(45, 157, 91))
                : new SolidColorBrush(Color.FromRgb(179, 116, 0)),
            FontSize = 12
        };
        Grid.SetColumn(changeLabel, 1);
        headingGrid.Children.Add(changeLabel);
        Grid.SetColumnSpan(headingGrid, 3);
        grid.Children.Add(headingGrid);

        var leftBlock = CreateDiffText(diff.Left);
        var rightBlock = CreateDiffText(diff.Right);
        var leftBorder = WrapDiffBlock(leftBlock);
        var rightBorder = WrapDiffBlock(rightBlock);
        Grid.SetRow(leftBorder, 1);
        Grid.SetColumn(leftBorder, 0);
        Grid.SetRow(rightBorder, 1);
        Grid.SetColumn(rightBorder, 2);
        grid.Children.Add(leftBorder);
        grid.Children.Add(rightBorder);

        section.Child = grid;
        return section;
    }

    private static Border WrapDiffBlock(TextBlock block)
        => new()
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 233, 241)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MinHeight = 110,
            Child = block
        };

    private static TextBlock CreateDiffText(IReadOnlyList<TextDiffSegment> segments)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 21
        };

        if (segments.Count == 0)
        {
            block.Inlines.Add(new Run("∅")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(129, 144, 167)),
                FontStyle = FontStyles.Italic
            });
            return block;
        }

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.Changed)
            {
                run.Background = new SolidColorBrush(Color.FromRgb(255, 236, 195));
                run.Foreground = new SolidColorBrush(Color.FromRgb(109, 73, 0));
                run.FontWeight = FontWeights.SemiBold;
            }
            block.Inlines.Add(run);
        }

        return block;
    }

    private sealed record ComparisonOption(LocalizationEntry Entry, string Label);
}
