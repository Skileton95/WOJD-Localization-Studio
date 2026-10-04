using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class EntryHistoryWindow : Window
{
    private readonly LocalizationEntry _entry;
    private readonly DataGrid _grid;

    public EntryHistoryWindow(LocalizationEntry entry)
    {
        _entry = entry;

        Title = $"История строки — {entry.Key}";
        Width = 980;
        Height = 560;
        MinWidth = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = $"{entry.Namespace} · {entry.Key}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(title);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = EntryHistoryService.GetHistory(entry)
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Время",
            Binding = new System.Windows.Data.Binding("Timestamp")
            {
                StringFormat = "HH:mm:ss"
            },
            Width = 90
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Причина",
            Binding = new System.Windows.Data.Binding("Reason"),
            Width = 180
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Было",
            Binding = new System.Windows.Data.Binding("Before"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Стало",
            Binding = new System.Windows.Data.Binding("After"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var restore = new Button
        {
            Content = "Восстановить «Было»",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        restore.Click += (_, _) => RestoreSelected();
        buttons.Children.Add(restore);

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        buttons.Children.Add(close);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    private void RestoreSelected()
    {
        if (_grid.SelectedItem is not EntryHistoryItem item)
        {
            MessageBox.Show(
                this,
                "Выберите запись истории.",
                "История строки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            "Восстановить перевод из состояния «Было» выбранной записи?",
            "История строки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        using (EntryHistoryService.BeginOperation("Восстановление из истории"))
        {
            EntryHistoryService.Record(
                _entry,
                _entry.Translation,
                item.Before,
                "Восстановление из истории");
            _entry.Translation = item.Before;
        }

        DialogResult = true;
    }
}
