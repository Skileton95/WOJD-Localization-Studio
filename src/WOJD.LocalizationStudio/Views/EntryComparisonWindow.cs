using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Views;

public sealed class EntryComparisonWindow : Window
{
    public EntryComparisonWindow(LocalizationEntry leftEntry, LocalizationEntry rightEntry)
    {
        Title = "Сравнение строк";
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(leftEntry, rightEntry);
    }

    private static UIElement BuildContent(LocalizationEntry leftEntry, LocalizationEntry rightEntry)
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(CreateEntryHeader(leftEntry, "Строка A", 0));
        header.Children.Add(CreateEntryHeader(rightEntry, "Строка B", 2));
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);

        var body = new StackPanel();
        body.Children.Add(CreateComparisonSection(
            "Исходный текст (CN)",
            leftEntry.OriginalDisplay,
            rightEntry.OriginalDisplay));
        body.Children.Add(CreateComparisonSection(
            "Перевод (RU)",
            leftEntry.Translation,
            rightEntry.Translation));
        scroll.Content = body;
        root.Children.Add(scroll);

        return root;
    }

    private static UIElement CreateEntryHeader(LocalizationEntry entry, string label, int column)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154))
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
            Text = string.IsNullOrWhiteSpace(entry.Namespace)
                ? $"ID: {entry.Index:N0}"
                : $"{entry.Namespace}  ·  ID: {entry.Index:N0}",
            Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 154)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(panel, column);
        return panel;
    }

    private static UIElement CreateComparisonSection(string title, string left, string right)
    {
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

        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetColumnSpan(heading, 3);
        grid.Children.Add(heading);

        var (leftBlock, rightBlock) = CreateDiffPair(left ?? string.Empty, right ?? string.Empty);
        Grid.SetRow(leftBlock, 1);
        Grid.SetColumn(leftBlock, 0);
        Grid.SetRow(rightBlock, 1);
        Grid.SetColumn(rightBlock, 2);
        grid.Children.Add(WrapDiffBlock(leftBlock));
        grid.Children.Add(WrapDiffBlock(rightBlock));

        section.Child = grid;
        return section;
    }

    private static Border WrapDiffBlock(TextBlock block)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 233, 241)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MinHeight = 110,
            Child = block
        };
        Grid.SetRow(border, Grid.GetRow(block));
        Grid.SetColumn(border, Grid.GetColumn(block));
        return border;
    }

    private static (TextBlock Left, TextBlock Right) CreateDiffPair(string left, string right)
    {
        var prefix = CommonPrefixLength(left, right);
        var suffix = CommonSuffixLength(left, right, prefix);

        var leftBlock = CreateDiffText(left, prefix, suffix);
        var rightBlock = CreateDiffText(right, prefix, suffix);
        return (leftBlock, rightBlock);
    }

    private static TextBlock CreateDiffText(string text, int prefix, int suffix)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 21
        };

        if (text.Length == 0)
        {
            block.Inlines.Add(new Run("∅")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(129, 144, 167)),
                FontStyle = FontStyles.Italic
            });
            return block;
        }

        if (prefix > 0)
            block.Inlines.Add(new Run(text[..prefix]));

        var changedLength = Math.Max(0, text.Length - prefix - suffix);
        if (changedLength > 0)
        {
            block.Inlines.Add(new Run(text.Substring(prefix, changedLength))
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 236, 195)),
                Foreground = new SolidColorBrush(Color.FromRgb(109, 73, 0))
            });
        }

        if (suffix > 0)
            block.Inlines.Add(new Run(text[^suffix..]));

        return block;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var i = 0;
        while (i < length && left[i] == right[i])
            i++;
        return i;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        var max = Math.Min(left.Length, right.Length) - prefix;
        var i = 0;
        while (i < max && left[left.Length - 1 - i] == right[right.Length - 1 - i])
            i++;
        return i;
    }
}
