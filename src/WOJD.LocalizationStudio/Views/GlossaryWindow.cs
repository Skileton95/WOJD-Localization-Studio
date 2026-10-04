using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class GlossaryWindow : Window
{
    private readonly ObservableCollection<GlossaryEntry> _entries;
    private readonly DataGrid _grid;

    public GlossaryWindow()
    {
        Title = "Глоссарий";
        Width = 960;
        Height = 620;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _entries = new ObservableCollection<GlossaryEntry>(
            GlossaryService.GetEntries().Select(x => new GlossaryEntry
            {
                Source = x.Source,
                Target = x.Target,
                Required = x.Required,
                Notes = x.Notes
            }));

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Обязательные термины участвуют в QA: если Source найден в Original, а Target отсутствует в переводе, строка получит предупреждение.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _grid = new DataGrid
        {
            ItemsSource = _entries,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            SelectionMode = DataGridSelectionMode.Extended
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Source",
            Binding = new Binding(nameof(GlossaryEntry.Source))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 220
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Target",
            Binding = new Binding(nameof(GlossaryEntry.Target))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 240
        });
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Обязательный",
            Binding = new Binding(nameof(GlossaryEntry.Required))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 110
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Примечание",
            Binding = new Binding(nameof(GlossaryEntry.Notes))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
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

        var add = new Button
        {
            Content = "Добавить",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        add.Click += (_, _) => _entries.Add(new GlossaryEntry());
        buttons.Children.Add(add);

        var remove = new Button
        {
            Content = "Удалить выбранные",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        remove.Click += (_, _) =>
        {
            foreach (var item in _grid.SelectedItems.OfType<GlossaryEntry>().ToList())
                _entries.Remove(item);
        };
        buttons.Children.Add(remove);

        var save = new Button
        {
            Content = "Сохранить",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            GlossaryService.Save(_entries);
            DialogResult = true;
        };
        buttons.Children.Add(save);

        buttons.Children.Add(new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }
}
