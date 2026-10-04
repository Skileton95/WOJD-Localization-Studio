using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class EditorQaEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, object>
        InstalledWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        InstalledWindows.Add(window, new object());

        window.Dispatcher.BeginInvoke(
            () => Install(window),
            DispatcherPriority.ContextIdle);
    }

    private static void Install(MainWindow window)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox ||
            translationBox.Parent is not Border editorBorder ||
            editorBorder.Parent is not Grid editorGrid)
        {
            return;
        }

        var footer =
            editorGrid.Children
                .OfType<Grid>()
                .FirstOrDefault(x =>
                    Grid.GetColumn(x) == 2 &&
                    Grid.GetRow(x) == 2);

        if (footer is null)
            return;

        InstallStructuralPanel(
            window,
            viewModel,
            translationBox,
            editorGrid,
            footer);

        InstallBulkBackupGuard(
            window,
            viewModel,
            footer);
    }

    private static void InstallStructuralPanel(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Grid editorGrid,
        Grid footer)
    {
        if (editorGrid.RowDefinitions.Count > 2)
            editorGrid.RowDefinitions[2].Height = GridLength.Auto;

        if (footer.RowDefinitions.Count == 0)
        {
            footer.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
        }

        foreach (UIElement child in footer.Children)
            Grid.SetRow(child, 1);

        var legacySummary =
            footer.Children
                .OfType<TextBlock>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 0);

        if (legacySummary is not null)
            legacySummary.Visibility = Visibility.Collapsed;

        var qaBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 7)
        };

        var qaGrid = new Grid();
        qaGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        qaGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        qaGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var qaText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 12.5
        };
        qaGrid.Children.Add(qaText);

        var previous = CreateQueueButton(
            window,
            "← Структура",
            "Предыдущая строка со сломанными тегами или плейсхолдерами (Shift+F7)");
        previous.Margin = new Thickness(0, 0, 6, 0);
        previous.Click += (_, _) =>
            NavigateStructuralIssue(
                window,
                viewModel,
                translationBox,
                -1);
        Grid.SetColumn(previous, 1);
        qaGrid.Children.Add(previous);

        var next = CreateQueueButton(
            window,
            "Структура →",
            "Следующая строка со сломанными тегами или плейсхолдерами (F7)");
        next.Click += (_, _) =>
            NavigateStructuralIssue(
                window,
                viewModel,
                translationBox,
                1);
        Grid.SetColumn(next, 2);
        qaGrid.Children.Add(next);

        qaBorder.Child = qaGrid;
        Grid.SetRow(qaBorder, 0);
        Grid.SetColumn(qaBorder, 0);
        Grid.SetColumnSpan(qaBorder, Math.Max(1, footer.ColumnDefinitions.Count));
        footer.Children.Add(qaBorder);

        void RefreshStructuralStatus()
        {
            var entry = viewModel.SelectedEntry;

            if (entry is null)
            {
                qaText.Text = "Структура: выберите строку для проверки.";
                qaText.ToolTip = null;
                SetOkAppearance(qaBorder, qaText);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Original))
            {
                qaText.Text = "— Исходный текст отсутствует — структура не проверяется.";
                qaText.ToolTip =
                    "Без Original невозможно подтвердить теги, плейсхолдеры, переносы и точность перевода.";
                SetUnavailableAppearance(qaBorder, qaText);
                return;
            }

            var result =
                StructuralQaService.Analyze(
                    entry.Original,
                    translationBox.Text);

            if (result.HasIssues)
            {
                qaText.Text = "⚠ " + result.Summary;
                qaText.ToolTip = result.Summary;
                SetErrorAppearance(qaBorder, qaText);
            }
            else
            {
                qaText.Text = "✓ Структура тегов и плейсхолдеров совпадает с оригиналом.";
                qaText.ToolTip = null;
                SetOkAppearance(qaBorder, qaText);
            }
        }

        translationBox.TextChanged += (_, _) => RefreshStructuralStatus();

        if (window.FindName("EntriesGrid") is DataGrid entriesGrid)
        {
            entriesGrid.SelectionChanged += (_, _) =>
                window.Dispatcher.BeginInvoke(
                    RefreshStructuralStatus,
                    DispatcherPriority.Background);
        }

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedEntry))
            {
                window.Dispatcher.BeginInvoke(
                    RefreshStructuralStatus,
                    DispatcherPriority.Background);
            }
        };

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.F7)
                return;

            var direction =
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                    ? -1
                    : 1;

            NavigateStructuralIssue(
                window,
                viewModel,
                translationBox,
                direction);
            args.Handled = true;
        };

        RefreshStructuralStatus();
    }

    private static void InstallBulkBackupGuard(
        MainWindow window,
        MainViewModel viewModel,
        Grid footer)
    {
        var bulkButton =
            footer.Children
                .OfType<Button>()
                .FirstOrDefault(button =>
                    string.Equals(
                        button.Content?.ToString(),
                        "Массово…",
                        StringComparison.Ordinal));

        if (bulkButton is null)
            return;

        bulkButton.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (viewModel.ActiveDocument is not LocalizationDocument document)
                return;

            try
            {
                BackupService.CreateBackup(
                    document.FilePath,
                    "before-bulk-autocorrect");
            }
            catch (Exception ex)
            {
                args.Handled = true;

                AppDialog.Show(
                    "Массовое автоисправление не запущено, потому что не удалось создать резервную копию.\n\n" +
                    ex.Message,
                    "Ошибка резервного копирования",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    window);
            }
        };
    }

    private static Button CreateQueueButton(
        MainWindow window,
        string content,
        string toolTip)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 5, 10, 5),
            ToolTip = toolTip,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (window.TryFindResource("SecondaryButton") is Style style)
            button.Style = style;

        return button;
    }

    private static void NavigateStructuralIssue(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        int direction)
    {
        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateSource();

        if (viewModel.ActiveDocument is not LocalizationDocument document ||
            document.Entries.Count == 0)
        {
            return;
        }

        var entries = document.Entries;
        var count = entries.Count;
        var start = viewModel.SelectedEntry is null
            ? (direction > 0 ? -1 : 0)
            : Math.Clamp(viewModel.SelectedEntry.Index - 1, 0, count - 1);

        LocalizationEntry? target = null;

        for (var step = 1; step <= count; step++)
        {
            var index =
                (start + direction * step) % count;

            if (index < 0)
                index += count;

            var candidate = entries[index];

            if (string.IsNullOrWhiteSpace(candidate.Original))
                continue;

            if (candidate.HasTagIssues ||
                candidate.HasPlaceholderIssues ||
                StructuralQaService
                    .Analyze(candidate.Original, candidate.Translation)
                    .HasIssues)
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
        {
            AppDialog.Show(
                "В текущем файле нет ошибок тегов или плейсхолдеров.",
                "Структурный QA",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        viewModel.StatusFilter = "Все";
        viewModel.SearchText = string.Empty;

        if (viewModel.ClearNamespaceFilterCommand.CanExecute(null))
            viewModel.ClearNamespaceFilterCommand.Execute(null);

        viewModel.EntriesView.Refresh();
        viewModel.SelectedEntry = target;
        viewModel.EntriesView.MoveCurrentTo(target);

        if (window.FindName("EntriesGrid") is DataGrid entriesGrid)
        {
            entriesGrid.UpdateLayout();
            entriesGrid.ScrollIntoView(target);
        }
    }

    private static void SetErrorAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(255, 244, 244));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(242, 184, 184));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(185, 48, 48));
        text.FontWeight = FontWeights.SemiBold;
    }

    private static void SetOkAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(247, 250, 252));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(226, 232, 240));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(94, 104, 119));
        text.FontWeight = FontWeights.Normal;
    }

    private static void SetUnavailableAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(255, 249, 235));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(238, 203, 126));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(145, 102, 17));
        text.FontWeight = FontWeights.SemiBold;
    }
}
