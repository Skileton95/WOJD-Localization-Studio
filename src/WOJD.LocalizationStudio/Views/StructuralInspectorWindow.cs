using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class StructuralInspectorWindow : Window
{
    public StructuralInspectorWindow(LocalizationEntry entry)
    {
        Title = $"Структура — {entry.Key}";
        Width = 1100;
        Height = 700;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var diff = StructuralDiffService.Analyze(entry.Original, entry.Translation);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"{entry.Namespace}  ·  {entry.Key}",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        };
        root.Children.Add(title);

        var stats = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 12),
            Text =
                $"Теги {diff.TranslationTagCount}/{diff.SourceTagCount}   ·   " +
                $"Плейсхолдеры {diff.TranslationPlaceholderCount}/{diff.SourcePlaceholderCount}   ·   " +
                $"Переносы {diff.TranslationNewLineCount}/{diff.SourceNewLineCount}   ·   " +
                (diff.HasIssues ? "есть различия" : "структура совпадает"),
            Foreground = diff.HasIssues ? Brushes.Firebrick : Brushes.ForestGreen
        };
        Grid.SetRow(stats, 1);
        root.Children.Add(stats);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(columns, 2);
        root.Children.Add(columns);

        var original = BuildPane("Оригинал", entry.Original, diff.SourceTokens);
        columns.Children.Add(original);

        var translation = BuildPane("Перевод", entry.Translation, diff.TranslationTokens);
        Grid.SetColumn(translation, 2);
        columns.Children.Add(translation);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Original)
                ? "Исходный текст отсутствует — структура не проверяется."
                : diff.Qa.Summary +
                  (diff.SourceNewLineCount == diff.TranslationNewLineCount
                      ? string.Empty
                      : $"   •   Переносы: {diff.SourceNewLineCount} → {diff.TranslationNewLineCount}"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 96, 110))
        };
        footer.Children.Add(details);

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        };
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
    }

    private static FrameworkElement BuildPane(
        string title,
        string text,
        IReadOnlyList<StructuralToken> tokens)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var editor = new RichTextBox
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13
        };

        var paragraph = new Paragraph { Margin = new Thickness(0) };

        foreach (var token in tokens)
        {
            var run = new Run(token.Value);

            switch (token.Kind)
            {
                case StructuralTokenKind.Tag:
                    run.Foreground = Brushes.RoyalBlue;
                    run.FontWeight = FontWeights.SemiBold;
                    run.Background = new SolidColorBrush(Color.FromRgb(232, 240, 255));
                    break;
                case StructuralTokenKind.Placeholder:
                    run.Foreground = Brushes.DarkMagenta;
                    run.FontWeight = FontWeights.SemiBold;
                    run.Background = new SolidColorBrush(Color.FromRgb(248, 235, 250));
                    break;
                case StructuralTokenKind.NewLine:
                    run.Foreground = Brushes.DarkOrange;
                    break;
            }

            paragraph.Inlines.Add(run);
        }

        if (tokens.Count == 0 && !string.IsNullOrEmpty(text))
            paragraph.Inlines.Add(new Run(text));

        editor.Document = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0)
        };
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }
}
