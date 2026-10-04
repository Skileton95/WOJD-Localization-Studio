using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class ExternalChangeWindow : Window
{
    public ExternalChangeWindow(ExternalFileDiffResult diff)
    {
        Title = "Файл изменён на диске";
        Width = 1120;
        Height = 650;
        MinWidth = 820;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = $"Изменённых переводов: {diff.Changes.Count:N0}. Отсутствует на диске: {diff.MissingRows:N0}. Новых строк на диске: {diff.AddedRows:N0}.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var grid = new DataGrid
        {
            ItemsSource = diff.Changes.Take(3000).ToList(),
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(ExternalTranslationDiff.Index)),
            Width = 70
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ключ",
            Binding = new Binding(nameof(ExternalTranslationDiff.Key)),
            Width = 210
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "В редакторе",
            Binding = new Binding(nameof(ExternalTranslationDiff.CurrentTranslation)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "На диске",
            Binding = new Binding(nameof(ExternalTranslationDiff.DiskTranslation)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            IsCancel = true
        };
        Grid.SetRow(close, 2);
        root.Children.Add(close);
        Content = root;
    }
}
