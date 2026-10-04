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
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class AdvancedEditorEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, State>
        InstalledWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        var state = new State();
        InstalledWindows.Add(window, state);

        window.Dispatcher.BeginInvoke(
            () => Install(window, state),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(MainWindow window, State state)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox)
        {
            return;
        }

        state.CurrentEntry = viewModel.SelectedEntry;
        state.Baseline = state.CurrentEntry?.Translation ?? string.Empty;

        InstallFooterButtons(window, viewModel, translationBox, state);
        InstallMenus(window, viewModel, translationBox, state);

        PropertyChangedEventHandler selectionHandler = (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.SelectedEntry))
                return;

            CaptureHistory(state);
            state.CurrentEntry = viewModel.SelectedEntry;
            state.Baseline = state.CurrentEntry?.Translation ?? string.Empty;
        };

        viewModel.PropertyChanged += selectionHandler;
        window.Closed += (_, _) =>
        {
            CaptureHistory(state);
            viewModel.PropertyChanged -= selectionHandler;
        };

        translationBox.LostKeyboardFocus += (_, _) =>
            window.Dispatcher.BeginInvoke(
                () => CaptureHistory(state),
                DispatcherPriority.Background);
    }

    private static void InstallFooterButtons(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
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

        while (footer.ColumnDefinitions.Count < 6)
        {
            footer.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
        }

        var history = CreateButton(
            window,
            "История",
            "История изменений выбранной строки в текущем сеансе");
        history.Margin = new Thickness(8, 0, 0, 0);
        history.Click += (_, _) =>
        {
            CommitTranslation(translationBox);
            CaptureHistory(state);

            if (viewModel.SelectedEntry is not LocalizationEntry entry)
                return;

            var dialog = new EntryHistoryWindow(entry)
            {
                Owner = window
            };

            if (dialog.ShowDialog() == true)
            {
                translationBox
                    .GetBindingExpression(TextBox.TextProperty)?
                    .UpdateTarget();
                viewModel.EntriesView.Refresh();
                state.Baseline = entry.Translation;
            }
        };
        Grid.SetColumn(history, 4);
        Grid.SetRow(history, 1);
        footer.Children.Add(history);

        var snapshots = CreateButton(
            window,
            "Снимки",
            "Создание и восстановление снимков текущего файла");
        snapshots.Margin = new Thickness(8, 0, 0, 0);
        snapshots.Click += (_, _) =>
            ShowSnapshots(window, viewModel, translationBox, state);
        Grid.SetColumn(snapshots, 5);
        Grid.SetRow(snapshots, 1);
        footer.Children.Add(snapshots);
    }

    private static void InstallMenus(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        var menu = FindVisualChild<Menu>(window);
        if (menu is null)
            return;

        var edit = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(x =>
                string.Equals(x.Header?.ToString(), "Правка", StringComparison.Ordinal));

        if (edit is not null)
        {
            edit.Items.Add(new Separator());

            var snapshots = new MenuItem
            {
                Header = "Снимки файла…"
            };
            snapshots.Click += (_, _) =>
                ShowSnapshots(window, viewModel, translationBox, state);
            edit.Items.Add(snapshots);
        }

        var tools = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(x =>
                string.Equals(x.Header?.ToString(), "Инструменты", StringComparison.Ordinal));

        if (tools is not null)
        {
            tools.Items.Add(new Separator());

            var profiles = new MenuItem
            {
                Header = "QA-профили…"
            };
            profiles.Click += (_, _) =>
            {
                var dialog = new QaProfilesWindow
                {
                    Owner = window
                };

                if (dialog.ShowDialog() != true ||
                    viewModel.ActiveDocument is not LocalizationDocument document)
                {
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    foreach (var entry in document.Entries)
                        entry.RefreshValidation();

                    viewModel.EntriesView.Refresh();
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };
            tools.Items.Add(profiles);
        }
    }

    private static void ShowSnapshots(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        CommitTranslation(translationBox);
        CaptureHistory(state);

        if (viewModel.ActiveDocument is not LocalizationDocument document)
        {
            AppDialog.Show(
                "Сначала откройте файл локализации.",
                "Снимки",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        var dialog = new SnapshotManagerWindow(document)
        {
            Owner = window
        };

        if (dialog.ShowDialog() == true && dialog.Restored)
        {
            translationBox
                .GetBindingExpression(TextBox.TextProperty)?
                .UpdateTarget();
            viewModel.EntriesView.Refresh();
            state.Baseline = viewModel.SelectedEntry?.Translation ?? string.Empty;
        }
    }

    private static void CaptureHistory(State state)
    {
        var entry = state.CurrentEntry;
        if (entry is null)
            return;

        var current = entry.Translation;

        if (!string.Equals(state.Baseline, current, StringComparison.Ordinal))
        {
            EntryHistoryService.Record(
                entry,
                state.Baseline,
                current,
                "Ручная правка");
            state.Baseline = current;
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

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private sealed class State
    {
        public LocalizationEntry? CurrentEntry { get; set; }
        public string Baseline { get; set; } = string.Empty;
    }
}
