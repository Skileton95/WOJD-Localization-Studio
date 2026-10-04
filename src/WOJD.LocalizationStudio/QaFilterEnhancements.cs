using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class QaFilterEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, FilterState>
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
            InstalledWindows.TryGetValue(window, out _) ||
            window.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var allButtons = FindVisualChildren<Button>(window).ToList();
        var errorsButton = allButtons.FirstOrDefault(
            x => ReferenceEquals(x.Command, viewModel.FilterErrorsCommand));

        if (errorsButton?.Parent is not Panel panel)
            return;

        var state = new FilterState(viewModel.EntriesView.Filter);
        InstalledWindows.Add(window, state);

        viewModel.EntriesView.Filter = item =>
            (state.BaseFilter?.Invoke(item) ?? true) &&
            MatchesMode(item, state.Mode);

        var tagButton = CreateFilterButton(
            window,
            "Теги",
            "Показать только строки с ошибками тегов <…>.");
        var placeholderButton = CreateFilterButton(
            window,
            "Плейсхолдеры",
            "Показать только строки с ошибками {…}, ${…} или %….");
        var newLineButton = CreateFilterButton(
            window,
            "Переносы",
            "Показать только строки, где количество переносов отличается от оригинала.");

        state.Buttons[QaFilterMode.Tags] = tagButton;
        state.Buttons[QaFilterMode.Placeholders] = placeholderButton;
        state.Buttons[QaFilterMode.NewLines] = newLineButton;

        tagButton.Click += (_, _) =>
            Activate(viewModel, state, QaFilterMode.Tags);
        placeholderButton.Click += (_, _) =>
            Activate(viewModel, state, QaFilterMode.Placeholders);
        newLineButton.Click += (_, _) =>
            Activate(viewModel, state, QaFilterMode.NewLines);

        var insertIndex = panel.Children.IndexOf(errorsButton) + 1;
        panel.Children.Insert(insertIndex++, tagButton);
        panel.Children.Insert(insertIndex++, placeholderButton);
        panel.Children.Insert(insertIndex, newLineButton);

        foreach (var button in allButtons.Where(
                     x => ReferenceEquals(x.Command, viewModel.FilterAllCommand) ||
                          ReferenceEquals(x.Command, viewModel.FilterTranslatedCommand) ||
                          ReferenceEquals(x.Command, viewModel.FilterUntranslatedCommand) ||
                          ReferenceEquals(x.Command, viewModel.FilterModifiedCommand) ||
                          ReferenceEquals(x.Command, viewModel.FilterErrorsCommand)))
        {
            button.Click += (_, _) => ResetMode(viewModel, state);
        }

        PropertyChangedEventHandler statusHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StatusFilter) &&
                !state.ChangingStatus &&
                state.Mode != QaFilterMode.None)
            {
                ResetMode(viewModel, state);
            }
        };

        viewModel.PropertyChanged += statusHandler;
        window.Closed += (_, _) =>
            viewModel.PropertyChanged -= statusHandler;

        UpdateButtonStates(state);
    }

    private static Button CreateFilterButton(
        MainWindow window,
        string text,
        string toolTip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = toolTip
        };

        if (window.TryFindResource("SecondaryButton") is Style style)
            button.Style = style;

        return button;
    }

    private static void Activate(
        MainViewModel viewModel,
        FilterState state,
        QaFilterMode mode)
    {
        state.ChangingStatus = true;

        try
        {
            viewModel.StatusFilter = "Все";
        }
        finally
        {
            state.ChangingStatus = false;
        }

        state.Mode = mode;
        UpdateButtonStates(state);
        viewModel.EntriesView.Refresh();
    }

    private static void ResetMode(
        MainViewModel viewModel,
        FilterState state)
    {
        if (state.Mode == QaFilterMode.None)
            return;

        state.Mode = QaFilterMode.None;
        UpdateButtonStates(state);
        viewModel.EntriesView.Refresh();
    }

    private static bool MatchesMode(
        object item,
        QaFilterMode mode)
    {
        if (mode == QaFilterMode.None)
            return true;

        if (item is not LocalizationEntry entry)
            return false;

        return mode switch
        {
            QaFilterMode.Tags => entry.HasTagIssues,
            QaFilterMode.Placeholders => entry.HasPlaceholderIssues,
            QaFilterMode.NewLines => entry.HasNewLineIssues,
            _ => true
        };
    }

    private static void UpdateButtonStates(FilterState state)
    {
        foreach (var pair in state.Buttons)
        {
            pair.Value.FontWeight =
                pair.Key == state.Mode
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;

            pair.Value.Opacity =
                pair.Key == state.Mode
                    ? 1.0
                    : 0.82;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
            yield break;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private enum QaFilterMode
    {
        None,
        Tags,
        Placeholders,
        NewLines
    }

    private sealed class FilterState
    {
        public FilterState(Predicate<object>? baseFilter)
        {
            BaseFilter = baseFilter;
        }

        public Predicate<object>? BaseFilter { get; }
        public QaFilterMode Mode { get; set; }
        public bool ChangingStatus { get; set; }
        public Dictionary<QaFilterMode, Button> Buttons { get; } = new();
    }
}
