using System.ComponentModel;
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

internal static class ReviewWorkflowEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, ReviewUiState>
        InstalledWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        var state = new ReviewUiState();
        InstalledWindows.Add(window, state);

        window.Dispatcher.BeginInvoke(
            () => Install(window, state),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(
        MainWindow window,
        ReviewUiState state)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("EntriesGrid") is not DataGrid entriesGrid ||
            window.FindName("TranslationBox") is not TextBox translationBox)
        {
            return;
        }

        InstallFilter(window, viewModel, state);
        InstallContextMenu(window, viewModel, entriesGrid, state);
        InstallReviewBar(window, viewModel, entriesGrid, translationBox, state);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedEntry))
            {
                window.Dispatcher.BeginInvoke(
                    () => RefreshUi(viewModel, state),
                    DispatcherPriority.Background);
            }
        };

        viewModel.PropertyChanged += handler;
        window.Closed += (_, _) => viewModel.PropertyChanged -= handler;

        translationBox.LostKeyboardFocus += (_, _) =>
            window.Dispatcher.BeginInvoke(
                () => RefreshUi(viewModel, state),
                DispatcherPriority.Background);

        RefreshUi(viewModel, state);
    }

    private static void InstallFilter(
        MainWindow window,
        MainViewModel viewModel,
        ReviewUiState state)
    {
        var errorsButton = FindVisualChildren<Button>(window)
            .FirstOrDefault(x => ReferenceEquals(x.Command, viewModel.FilterErrorsCommand));

        if (errorsButton?.Parent is not Panel panel)
            return;

        state.BaseFilter = viewModel.EntriesView.Filter;
        viewModel.EntriesView.Filter = item =>
            (state.BaseFilter?.Invoke(item) ?? true) &&
            MatchesReviewFilter(viewModel, item, state.Filter);

        var combo = new ComboBox
        {
            Width = 178,
            Height = 34,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Фильтр по статусу ручной проверки"
        };

        combo.Items.Add(new ReviewFilterOption("Проверка: Все", null));
        combo.Items.Add(new ReviewFilterOption("Не проверено", ReviewState.Unreviewed));
        combo.Items.Add(new ReviewFilterOption("Проверено", ReviewState.Reviewed));
        combo.Items.Add(new ReviewFilterOption("Требует правки", ReviewState.NeedsFix));
        combo.Items.Add(new ReviewFilterOption("Пропустить", ReviewState.Skipped));
        combo.SelectedIndex = 0;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ReviewFilterOption option)
                return;

            state.Filter = option.State;
            viewModel.EntriesView.Refresh();
        };

        state.FilterCombo = combo;
        panel.Children.Add(combo);
    }

    private static void InstallContextMenu(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        ReviewUiState state)
    {
        var menu = entriesGrid.ContextMenu ?? new ContextMenu();
        entriesGrid.ContextMenu = menu;

        menu.Items.Add(new Separator());

        var reviewMenu = new MenuItem
        {
            Header = "Статус проверки"
        };

        foreach (var option in new[]
                 {
                     ("Не проверено", ReviewState.Unreviewed),
                     ("Проверено", ReviewState.Reviewed),
                     ("Требует правки", ReviewState.NeedsFix),
                     ("Пропустить", ReviewState.Skipped)
                 })
        {
            var item = new MenuItem { Header = option.Item1 };
            item.Click += (_, _) =>
            {
                var entries = entriesGrid.SelectedItems
                    .OfType<LocalizationEntry>()
                    .ToList();

                if (entries.Count == 0 && viewModel.SelectedEntry is not null)
                    entries.Add(viewModel.SelectedEntry);

                if (entries.Count == 0 || viewModel.ActiveDocument is null)
                    return;

                ReviewWorkflowService.SetStatus(
                    viewModel.ActiveDocument.FilePath,
                    entries,
                    option.Item2);

                RefreshUi(viewModel, state);
                viewModel.EntriesView.Refresh();
            };
            reviewMenu.Items.Add(item);
        }

        menu.Items.Add(reviewMenu);
    }

    private static void InstallReviewBar(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox,
        ReviewUiState state)
    {
        if (translationBox.Parent is not Border editorBorder ||
            editorBorder.Parent is not Grid editorGrid)
        {
            return;
        }

        var footer = editorGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(x => Grid.GetColumn(x) == 2 && Grid.GetRow(x) == 2);

        if (footer is null)
            return;

        while (footer.RowDefinitions.Count < 3)
        {
            footer.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
        }

        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 7, 0, 0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusLabel = new TextBlock
        {
            Text = "Проверка:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            FontWeight = FontWeights.SemiBold
        };
        grid.Children.Add(statusLabel);

        var statusCombo = new ComboBox
        {
            Width = 150,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        statusCombo.Items.Add(new ReviewStatusOption("Не проверено", ReviewState.Unreviewed));
        statusCombo.Items.Add(new ReviewStatusOption("Проверено", ReviewState.Reviewed));
        statusCombo.Items.Add(new ReviewStatusOption("Требует правки", ReviewState.NeedsFix));
        statusCombo.Items.Add(new ReviewStatusOption("Пропустить", ReviewState.Skipped));
        Grid.SetColumn(statusCombo, 1);
        grid.Children.Add(statusCombo);
        state.StatusCombo = statusCombo;

        var countsText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(94, 104, 119)),
            Margin = new Thickness(12, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(countsText, 2);
        grid.Children.Add(countsText);
        state.CountsText = countsText;

        var nextUnreviewed = CreateButton(
            window,
            "→ Непроверенная",
            "Перейти к следующей непроверенной строке");
        nextUnreviewed.Margin = new Thickness(0, 0, 6, 0);
        nextUnreviewed.Click += (_, _) =>
            NavigateNextUnreviewed(window, viewModel, entriesGrid, translationBox);
        Grid.SetColumn(nextUnreviewed, 3);
        grid.Children.Add(nextUnreviewed);

        var needsFix = CreateButton(
            window,
            "⚠ Правка и дальше",
            "Отметить строку как требующую правки и перейти к следующей непроверенной");
        needsFix.Margin = new Thickness(0, 0, 6, 0);
        needsFix.Click += (_, _) =>
        {
            CommitTranslation(translationBox);
            SetCurrentStatus(viewModel, ReviewState.NeedsFix);
            RefreshUi(viewModel, state);
            NavigateNextUnreviewed(window, viewModel, entriesGrid, translationBox);
        };
        Grid.SetColumn(needsFix, 4);
        grid.Children.Add(needsFix);

        var reviewed = CreateButton(
            window,
            "✓ Проверено и дальше",
            "Отметить текущий перевод проверенным и перейти к следующей непроверенной строке");
        if (window.TryFindResource("PrimaryButton") is Style primary)
            reviewed.Style = primary;
        reviewed.Click += (_, _) =>
        {
            CommitTranslation(translationBox);
            SetCurrentStatus(viewModel, ReviewState.Reviewed);
            RefreshUi(viewModel, state);
            NavigateNextUnreviewed(window, viewModel, entriesGrid, translationBox);
        };
        Grid.SetColumn(reviewed, 5);
        grid.Children.Add(reviewed);

        statusCombo.SelectionChanged += (_, _) =>
        {
            if (state.UpdatingStatus ||
                statusCombo.SelectedItem is not ReviewStatusOption option ||
                viewModel.SelectedEntry is null ||
                viewModel.ActiveDocument is null)
            {
                return;
            }

            CommitTranslation(translationBox);
            ReviewWorkflowService.SetStatus(
                viewModel.ActiveDocument.FilePath,
                viewModel.SelectedEntry,
                option.State);
            RefreshUi(viewModel, state);
            viewModel.EntriesView.Refresh();
        };

        bar.Child = grid;
        Grid.SetRow(bar, 2);
        Grid.SetColumn(bar, 0);
        Grid.SetColumnSpan(bar, Math.Max(1, footer.ColumnDefinitions.Count));
        footer.Children.Add(bar);
        state.Bar = bar;
    }

    private static void SetCurrentStatus(
        MainViewModel viewModel,
        ReviewState state)
    {
        if (viewModel.ActiveDocument is null ||
            viewModel.SelectedEntry is null)
        {
            return;
        }

        ReviewWorkflowService.SetStatus(
            viewModel.ActiveDocument.FilePath,
            viewModel.SelectedEntry,
            state);
    }

    private static void NavigateNextUnreviewed(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox)
    {
        CommitTranslation(translationBox);

        var document = viewModel.ActiveDocument;
        if (document is null || document.Entries.Count == 0)
            return;

        var entries = document.Entries;
        var current = viewModel.SelectedEntry;
        var start = current is null
            ? -1
            : entries.IndexOf(current);

        LocalizationEntry? target = null;

        for (var offset = 1; offset <= entries.Count; offset++)
        {
            var index = (start + offset) % entries.Count;
            var candidate = entries[index];

            if (ReviewWorkflowService.GetStatus(
                    document.FilePath,
                    candidate) == ReviewState.Unreviewed)
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
        {
            AppDialog.Show(
                "Непроверенных строк в текущем файле больше нет.",
                "Проверка перевода",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        viewModel.SelectedEntry = target;
        viewModel.EntriesView.MoveCurrentTo(target);
        entriesGrid.UpdateLayout();
        entriesGrid.SelectedItem = target;
        entriesGrid.ScrollIntoView(target);
        translationBox.Focus();
    }

    private static bool MatchesReviewFilter(
        MainViewModel viewModel,
        object item,
        ReviewState? filter)
    {
        if (filter is null)
            return true;

        if (item is not LocalizationEntry entry ||
            viewModel.ActiveDocument is null)
        {
            return false;
        }

        return ReviewWorkflowService.GetStatus(
                   viewModel.ActiveDocument.FilePath,
                   entry) == filter.Value;
    }

    private static void RefreshUi(
        MainViewModel viewModel,
        ReviewUiState state)
    {
        var document = viewModel.ActiveDocument;
        var entry = viewModel.SelectedEntry;

        state.UpdatingStatus = true;

        try
        {
            if (state.StatusCombo is not null)
            {
                var status = document is null || entry is null
                    ? ReviewState.Unreviewed
                    : ReviewWorkflowService.GetStatus(document.FilePath, entry);

                state.StatusCombo.SelectedItem =
                    state.StatusCombo.Items
                        .OfType<ReviewStatusOption>()
                        .FirstOrDefault(x => x.State == status);
                state.StatusCombo.IsEnabled = entry is not null;
            }
        }
        finally
        {
            state.UpdatingStatus = false;
        }

        if (state.CountsText is not null)
        {
            if (document is null)
            {
                state.CountsText.Text = string.Empty;
            }
            else
            {
                var counts = ReviewWorkflowService.GetCounts(
                    document.FilePath,
                    document.Entries);

                state.CountsText.Text =
                    $"Проверено {counts.Reviewed:N0} · Не проверено {counts.Unreviewed:N0} · " +
                    $"Требует правки {counts.NeedsFix:N0} · Пропущено {counts.Skipped:N0}";
            }
        }
    }

    private static Button CreateButton(
        MainWindow window,
        string text,
        string toolTip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5, 10, 5),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = toolTip
        };

        if (window.TryFindResource("SecondaryButton") is Style style)
            button.Style = style;

        return button;
    }

    private static void CommitTranslation(TextBox translationBox)
        => translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateSource();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private sealed class ReviewUiState
    {
        public Predicate<object>? BaseFilter { get; set; }
        public ReviewState? Filter { get; set; }
        public ComboBox? FilterCombo { get; set; }
        public ComboBox? StatusCombo { get; set; }
        public TextBlock? CountsText { get; set; }
        public Border? Bar { get; set; }
        public bool UpdatingStatus { get; set; }
    }

    private sealed record ReviewFilterOption(
        string Text,
        ReviewState? State)
    {
        public override string ToString() => Text;
    }

    private sealed record ReviewStatusOption(
        string Text,
        ReviewState State)
    {
        public override string ToString() => Text;
    }
}
