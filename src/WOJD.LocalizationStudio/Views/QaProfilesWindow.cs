using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class QaProfilesWindow : Window
{
    private readonly ObservableCollection<QaProfileRule> _rules;
    private readonly DataGrid _grid;

    public QaProfilesWindow()
    {
        Title = "QA-профили";
        Width = 920;
        Height = 520;
        MinWidth = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _rules = new ObservableCollection<QaProfileRule>(
            QaProfileService.GetRules()
                .Select(Clone));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var info = new TextBlock
        {
            Text =
                "Правила применяются по маскам Namespace и Key. Символ * означает любое значение. " +
                "Пустой лимит длины отключает проверку. Для переносов: пусто — не проверять, ✓ — разрешены, ✕ — запрещены.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(info);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = _rules,
            SelectionMode = DataGridSelectionMode.Single
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Название",
            Binding = new System.Windows.Data.Binding("Name")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = 190
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Namespace",
            Binding = new System.Windows.Data.Binding("NamespacePattern")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = 180
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Key",
            Binding = new System.Windows.Data.Binding("KeyPattern")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Макс. символов",
            Binding = new System.Windows.Data.Binding("MaxCharacters")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = 120
        });
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Переносы",
            IsThreeState = true,
            Binding = new System.Windows.Data.Binding("AllowNewLines")
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = 90
        });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var bottom = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var add = new Button
        {
            Content = "+ Правило",
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        add.Click += (_, _) =>
        {
            var rule = new QaProfileRule
            {
                Name = "Новое правило",
                NamespacePattern = "*",
                KeyPattern = "*"
            };
            _rules.Add(rule);
            _grid.SelectedItem = rule;
            _grid.ScrollIntoView(rule);
        };
        bottom.Children.Add(add);

        var remove = new Button
        {
            Content = "− Удалить",
            Padding = new Thickness(12, 7, 12, 7)
        };
        remove.Click += (_, _) =>
        {
            if (_grid.SelectedItem is QaProfileRule rule)
                _rules.Remove(rule);
        };
        Grid.SetColumn(remove, 1);
        bottom.Children.Add(remove);

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
            QaProfileService.SaveRules(_rules);
            DialogResult = true;
        };
        Grid.SetColumn(save, 3);
        bottom.Children.Add(save);

        var cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        };
        Grid.SetColumn(cancel, 4);
        bottom.Children.Add(cancel);

        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;
    }

    private static QaProfileRule Clone(QaProfileRule source)
        => new()
        {
            Name = source.Name,
            NamespacePattern = source.NamespacePattern,
            KeyPattern = source.KeyPattern,
            MaxCharacters = source.MaxCharacters,
            AllowNewLines = source.AllowNewLines
        };
}
