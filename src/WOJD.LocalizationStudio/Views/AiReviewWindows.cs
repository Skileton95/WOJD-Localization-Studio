using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class AiSettingsWindow : Window
{
    private readonly PasswordBox _apiKey;
    private readonly TextBox _model;

    public string ApiKey => _apiKey.Password;
    public string Model => _model.Text.Trim();

    public AiSettingsWindow()
    {
        Title = "Настройка ИИ-проверки";
        Width = 560;
        Height = 310;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var info = new TextBlock
        {
            Text =
                "Ключ используется только в текущем сеансе и не записывается в проект. " +
                "Можно вместо этого задать переменную окружения OPENAI_API_KEY.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(info);

        var apiLabel = new TextBlock
        {
            Text = "OpenAI API key",
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(apiLabel, 1);
        root.Children.Add(apiLabel);

        _apiKey = new PasswordBox
        {
            Margin = new Thickness(0, 6, 0, 12),
            Padding = new Thickness(8, 6, 8, 6)
        };
        Grid.SetRow(_apiKey, 2);
        root.Children.Add(_apiKey);

        var modelPanel = new Grid();
        modelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var modelLabel = new TextBlock
        {
            Text = "Модель:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        modelPanel.Children.Add(modelLabel);

        _model = new TextBox
        {
            Text = AiCorrectionService.Model,
            Padding = new Thickness(8, 6, 8, 6)
        };
        Grid.SetColumn(_model, 1);
        modelPanel.Children.Add(_model);
        Grid.SetRow(modelPanel, 3);
        root.Children.Add(modelPanel);

        var note = new TextBlock
        {
            Text = AiCorrectionService.IsConfigured
                ? "Ключ уже доступен. Оставьте поле API key пустым, чтобы использовать текущий ключ/OPENAI_API_KEY."
                : "Ключ пока не задан.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(note, 4);
        root.Children.Add(note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var apply = new Button
        {
            Content = "Применить",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        apply.Click += (_, _) =>
        {
            if (!AiCorrectionService.IsConfigured &&
                string.IsNullOrWhiteSpace(_apiKey.Password))
            {
                MessageBox.Show(
                    this,
                    "Введите API key или задайте OPENAI_API_KEY.",
                    "ИИ-проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_model.Text))
            {
                MessageBox.Show(
                    this,
                    "Укажите модель.",
                    "ИИ-проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        };
        buttons.Children.Add(apply);

        buttons.Children.Add(new Button
        {
            Content = "Отмена",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });

        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);
        Content = root;
    }
}

public sealed class AiReviewPreviewWindow : Window
{
    public bool ApplySuggestion { get; private set; }

    public AiReviewPreviewWindow(
        LocalizationEntry entry,
        AiReviewResult result)
    {
        Title = "ИИ-проверка перевода";
        Width = 940;
        Height = 620;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"{entry.Namespace} · {entry.Key} · {result.Model}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(header);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        columns.Children.Add(CreateTextPanel("Текущий перевод", entry.Translation));
        var suggestion = CreateTextPanel("Предложение ИИ", result.Translation);
        Grid.SetColumn(suggestion, 1);
        columns.Children.Add(suggestion);
        Grid.SetRow(columns, 1);
        root.Children.Add(columns);

        var reason = new TextBlock
        {
            Text = "Причина: " + result.Reason,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        };
        Grid.SetRow(reason, 2);
        root.Children.Add(reason);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (result.Changed)
        {
            var apply = new Button
            {
                Content = "Применить предложение",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            apply.Click += (_, _) =>
            {
                ApplySuggestion = true;
                DialogResult = true;
            };
            buttons.Children.Add(apply);
        }

        buttons.Children.Add(new Button
        {
            Content = result.Changed ? "Оставить текущий" : "Закрыть",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });

        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
    }

    private static Border CreateTextPanel(string title, string text)
    {
        var border = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(4)
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0)
        };
        Grid.SetRow(box, 1);
        panel.Children.Add(box);
        border.Child = panel;
        return border;
    }
}

public sealed class AiBatchSuggestion
{
    public bool IsSelected { get; set; } = true;
    public required LocalizationEntry Entry { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
    public required string Reason { get; init; }
}

public sealed class AiBatchPreviewWindow : Window
{
    private readonly DataGrid _grid;

    public IReadOnlyList<AiBatchSuggestion> SelectedSuggestions
        => _grid.ItemsSource is IEnumerable<AiBatchSuggestion> rows
            ? rows.Where(x => x.IsSelected).ToList()
            : [];

    public AiBatchPreviewWindow(IReadOnlyList<AiBatchSuggestion> suggestions)
    {
        Title = "Предпросмотр ИИ-исправлений";
        Width = 1180;
        Height = 650;
        MinWidth = 860;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = $"Предложено изменений: {suggestions.Count:N0}. Снимите галочки с изменений, которые не нужно применять.",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        });

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            ItemsSource = suggestions,
            SelectionMode = DataGridSelectionMode.Single
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "✓",
            Binding = new Binding("IsSelected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 44
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Ключ",
            Binding = new Binding("Entry.Key"),
            IsReadOnly = true,
            Width = 190
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Было",
            Binding = new Binding("Before"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Станет",
            Binding = new Binding("After"),
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Причина",
            Binding = new Binding("Reason"),
            IsReadOnly = true,
            Width = 260
        });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var apply = new Button
        {
            Content = "Применить выбранные",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        apply.Click += (_, _) =>
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            DialogResult = true;
        };
        buttons.Children.Add(apply);
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
