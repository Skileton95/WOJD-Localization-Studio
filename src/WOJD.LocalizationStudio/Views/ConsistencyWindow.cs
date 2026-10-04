using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class ConsistencyWindow : Window
{
    private readonly LocalizationDocument _document;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private CancellationTokenSource? _cts;

    public ConsistencyWindow(LocalizationDocument document)
    {
        _document = document;
        Title = "Согласованность переводов";
        Width = 1120;
        Height = 680;
        MinWidth = 820;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Одинаковый Original, но разные русские переводы. Выберите группу, чтобы при необходимости унифицировать её самым частым вариантом.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var progressPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        progressPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        progressPanel.Children.Add(_status);
        _progress = new ProgressBar
        {
            Width = 260,
            Height = 7,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_progress, 1);
        progressPanel.Children.Add(_progress);
        _cancel = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(10, 5, 10, 5),
            IsEnabled = false
        };
        _cancel.Click += (_, _) => _cts?.Cancel();
        progressPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_cancel, 2);
        progressPanel.Children.Add(_cancel);
        Grid.SetRow(progressPanel, 1);
        root.Children.Add(progressPanel);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Original",
            Binding = new Binding(nameof(ConsistencyIssue.Original)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Встречается",
            Binding = new Binding(nameof(ConsistencyIssue.TotalOccurrences))
            {
                StringFormat = "N0"
            },
            Width = 100
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Вариантов",
            Binding = new Binding("Variants.Count"),
            Width = 90
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Самый частый перевод",
            Binding = new Binding("Variants[0].Translation"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var unify = new Button
        {
            Content = "Унифицировать самым частым",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        unify.Click += (_, _) => UnifySelected();
        buttons.Children.Add(unify);
        buttons.Children.Add(new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        Loaded += async (_, _) => await AnalyzeAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task AnalyzeAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _cancel.IsEnabled = true;
        _status.Text = "Анализ файла…";
        _progress.Value = 0;

        var progress = new Progress<(int Current, int Total)>(p =>
        {
            _progress.Value = p.Total == 0 ? 0 : p.Current * 100.0 / p.Total;
            _status.Text = $"Анализ: {p.Current:N0} / {p.Total:N0}";
        });

        try
        {
            var issues = await ConsistencyService.AnalyzeAsync(
                _document,
                progress,
                _cts.Token);
            _grid.ItemsSource = issues;
            _status.Text = $"Найдено групп с разными переводами: {issues.Count:N0}";
            _progress.Value = 100;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Анализ отменён.";
        }
        finally
        {
            _cancel.IsEnabled = false;
        }
    }

    private void UnifySelected()
    {
        if (_grid.SelectedItem is not ConsistencyIssue issue || issue.Variants.Count == 0)
        {
            MessageBox.Show(this, "Выберите группу.", "Согласованность", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preferred = issue.Variants[0].Translation;
        var affected = _document.Entries
            .Where(x => string.Equals(x.Original.Trim(), issue.Original, StringComparison.Ordinal) &&
                        !string.Equals(x.Translation.Trim(), preferred, StringComparison.Ordinal))
            .ToList();

        if (affected.Count == 0)
            return;

        var answer = MessageBox.Show(
            this,
            $"Заменить {affected.Count:N0} переводов на:\n\n{preferred}\n\nПеред изменением будет создан снимок.",
            "Унификация переводов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        var snapshot = SnapshotService.CreateSnapshot(_document, "before-consistency-unify");
        using (EntryHistoryService.BeginOperation("Унификация одинакового Original"))
        {
            foreach (var entry in affected)
                entry.Translation = preferred;
        }
        ProjectHistoryService.Record(
            _document.FilePath,
            "Унификация перевода",
            affected.Count,
            $"Original: {issue.Original}",
            snapshot.Path);

        DialogResult = true;
    }
}
