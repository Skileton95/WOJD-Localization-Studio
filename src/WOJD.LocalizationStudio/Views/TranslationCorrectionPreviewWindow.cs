using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class TranslationCorrectionPreviewWindow : Window
{
    private readonly ObservableCollection<PreviewItem> _items;
    private readonly TextBlock _selectionSummary;

    public TranslationCorrectionPreviewWindow(
        int totalEntries,
        TranslationCorrectionPlan plan)
    {
        Title = "Предпросмотр автоисправления";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = new ObservableCollection<PreviewItem>(
            plan.Changes.Select(change => new PreviewItem(change)));

        var root = new Grid
        {
            Margin = new Thickness(18)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Массовое автоисправление всего файла",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(title);

        var summaryPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(summaryPanel, 1);

        summaryPanel.Children.Add(new TextBlock
        {
            Text =
                $"Всего строк: {totalEntries:N0}   •   " +
                $"Предложено изменений: {plan.AffectedEntries:N0}   •   " +
                $"Исправлений: {plan.TotalFixes:N0}   •   " +
                $"Требуют проверки: {plan.ReviewItems.Count:N0}",
            FontWeight = FontWeights.SemiBold
        });

        summaryPanel.Children.Add(new TextBlock
        {
            Text = plan.ReviewItems.Count == 0
                ? "Автоматически применяются только детерминированные исправления."
                : "Неоднозначные ошибки тегов и плейсхолдеров автоматически не меняются и остаются в QA для ручной проверки.",
            Margin = new Thickness(0, 5, 0, 0),
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });

        _selectionSummary = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            FontWeight = FontWeights.SemiBold
        };
        summaryPanel.Children.Add(_selectionSummary);
        root.Children.Add(summaryPanel);

        var grid = new DataGrid
        {
            ItemsSource = _items,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            Margin = new Thickness(0, 0, 0, 14)
        };

        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "✓",
            Width = 44,
            Binding = new Binding(nameof(PreviewItem.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Width = 70,
            IsReadOnly = true,
            Binding = new Binding(nameof(PreviewItem.Index))
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ключ",
            Width = 210,
            IsReadOnly = true,
            Binding = new Binding(nameof(PreviewItem.Key))
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Было",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
            Binding = new Binding(nameof(PreviewItem.Before))
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Станет",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
            Binding = new Binding(nameof(PreviewItem.After))
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Причина",
            Width = 240,
            IsReadOnly = true,
            Binding = new Binding(nameof(PreviewItem.Rules))
        });

        Grid.SetRow(grid, 2);
        root.Children.Add(grid);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(footer, 3);

        var selectAll = new Button
        {
            Content = "Выбрать все",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        selectAll.Click += (_, _) =>
        {
            foreach (var item in _items)
                item.IsSelected = true;
            grid.Items.Refresh();
            RefreshSelectionSummary();
        };
        footer.Children.Add(selectAll);

        var clearAll = new Button
        {
            Content = "Снять все",
            Padding = new Thickness(14, 7, 14, 7)
        };
        clearAll.Click += (_, _) =>
        {
            foreach (var item in _items)
                item.IsSelected = false;
            grid.Items.Refresh();
            RefreshSelectionSummary();
        };
        Grid.SetColumn(clearAll, 1);
        footer.Children.Add(clearAll);

        var cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(8, 0, 8, 0),
            IsCancel = true
        };
        Grid.SetColumn(cancel, 3);
        footer.Children.Add(cancel);

        var apply = new Button
        {
            Content = "Применить выбранные",
            Padding = new Thickness(18, 7, 18, 7),
            IsDefault = true
        };
        apply.Click += (_, _) =>
        {
            SelectedChanges = _items
                .Where(x => x.IsSelected)
                .Select(x => x.Change)
                .ToArray();

            if (SelectedChanges.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Не выбрано ни одного изменения.",
                    "Автоисправление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        };
        Grid.SetColumn(apply, 4);
        footer.Children.Add(apply);

        grid.CurrentCellChanged += (_, _) => RefreshSelectionSummary();
        grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(RefreshSelectionSummary);

        root.Children.Add(footer);
        Content = root;
        RefreshSelectionSummary();
    }

    public IReadOnlyList<TranslationCorrectionChange> SelectedChanges { get; private set; }
        = Array.Empty<TranslationCorrectionChange>();

    private void RefreshSelectionSummary()
    {
        _selectionSummary.Text =
            $"Будет применено: {_items.Count(x => x.IsSelected):N0} из {_items.Count:N0}";
    }

    private sealed class PreviewItem
    {
        public PreviewItem(TranslationCorrectionChange change)
        {
            Change = change;
        }

        public TranslationCorrectionChange Change { get; }
        public bool IsSelected { get; set; } = true;
        public int Index => Change.Entry.Index;
        public string Key => $"{Change.Entry.Namespace}:{Change.Entry.Key}";
        public string Before => Change.Before;
        public string After => Change.After;
        public string Rules => string.Join(", ", Change.AppliedRules);
    }
}
