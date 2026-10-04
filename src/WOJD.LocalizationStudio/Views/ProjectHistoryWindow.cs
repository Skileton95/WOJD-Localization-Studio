using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class ProjectHistoryWindow : Window
{
    public ProjectHistoryWindow(string? filePath)
    {
        Title = "История проекта";
        Width = 1040;
        Height = 620;
        MinWidth = 760;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = filePath is null
                ? "Последние операции редактора"
                : "Последние операции для текущего файла",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var rows = ProjectHistoryService.LoadRecent(filePath, 500);
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Время",
            Binding = new Binding(nameof(ProjectHistoryRecord.Timestamp))
            {
                StringFormat = "dd.MM.yyyy HH:mm:ss"
            },
            Width = 160
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Операция",
            Binding = new Binding(nameof(ProjectHistoryRecord.Operation)),
            Width = 220
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Строк",
            Binding = new Binding(nameof(ProjectHistoryRecord.AffectedEntries))
            {
                StringFormat = "N0"
            },
            Width = 90
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Детали",
            Binding = new Binding(nameof(ProjectHistoryRecord.Details)),
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
