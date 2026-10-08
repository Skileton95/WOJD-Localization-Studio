using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class EditorWorkspaceHost
{
    private static readonly ConditionalWeakTable<MainWindow, HostState> States = new();

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
        if (sender is not MainWindow window || States.TryGetValue(window, out _))
            return;

        window.Dispatcher.BeginInvoke(
            () => Install(window),
            DispatcherPriority.Loaded);
    }

    private static void Install(MainWindow window)
    {
        if (States.TryGetValue(window, out _) ||
            window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox legacyTranslationBox ||
            window.FindName("EntriesGrid") is not DataGrid entriesGrid ||
            legacyTranslationBox.Parent is not Border translationBorder ||
            translationBorder.Parent is not Grid legacyEditorGrid ||
            legacyEditorGrid.Parent is not Border legacyEditorBorder ||
            legacyEditorBorder.Parent is not Grid contentGrid)
        {
            return;
        }

        var legacyRow = Grid.GetRow(legacyEditorBorder);
        contentGrid.Children.Remove(legacyEditorBorder);

        if (legacyRow < 0 || legacyRow >= contentGrid.RowDefinitions.Count)
            return;

        var editorRow = contentGrid.RowDefinitions[legacyRow];
        var workspace = new EditorWorkspaceControl();
        Grid.SetRow(workspace, legacyRow);
        contentGrid.Children.Add(workspace);

        var state = new HostState(
            editorRow,
            workspace,
            entriesGrid,
            viewModel);
        States.Add(window, state);

        workspace.Attach(window, viewModel, entriesGrid);
        workspace.CollapseRequested += (_, _) => ApplyLayout(state);
        workspace.SettingsChanged += (_, _) => ApplyLayout(state);
        workspace.FocusModeRequested += (_, _) => ToggleFocusMode(window, state);

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.F10)
            {
                ToggleFocusMode(window, state);
                args.Handled = true;
                return;
            }

            HandlePreviewKeyDown(state, args);
        };
        window.Closed += (_, _) => workspace.CommitTranslation();

        InstallMenus(window, state);
        ApplyLayout(state);
        ApplyFocusMode(window, state);
    }

    private static void ApplyLayout(HostState state)
    {
        var settings = EditorSettingsService.Current;
        state.EditorRow.Height = new GridLength(state.Workspace.DesiredEditorHeight);

        SetColumnVisibility(state.EntriesGrid, "Namespace", settings.ShowNamespaceColumn);
        SetColumnVisibility(state.EntriesGrid, "Оригинал", settings.ShowOriginalColumn);
        SetColumnVisibility(state.EntriesGrid, "Статус", settings.ShowStatusColumn);
    }

    private static void SetColumnVisibility(DataGrid grid, string header, bool visible)
    {
        var column = grid.Columns.FirstOrDefault(x =>
            string.Equals(x.Header?.ToString(), header, StringComparison.Ordinal));
        if (column is not null)
            column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void HandlePreviewKeyDown(HostState state, KeyEventArgs args)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && args.Key == Key.Z && state.Workspace.TryBulkUndo())
        {
            args.Handled = true;
            return;
        }

        if (ctrl && args.Key == Key.Y && state.Workspace.TryBulkRedo())
        {
            args.Handled = true;
            return;
        }

        if (ctrl && (args.Key == Key.S || args.Key == Key.G || args.Key == Key.O) ||
            args.Key == Key.F6 || args.Key == Key.F7)
        {
            state.Workspace.CommitTranslation();
        }
    }

    private static void ToggleFocusMode(MainWindow window, HostState state)
    {
        var settings = EditorSettingsService.Current;
        settings.FocusMode = !settings.FocusMode;
        EditorSettingsService.Save(settings);
        ApplyFocusMode(window, state);
        state.Workspace.ApplySettings();
    }

    private static void ApplyFocusMode(MainWindow window, HostState state)
    {
        var enabled = EditorSettingsService.Current.FocusMode;
        var rootGrid = window.Content as Grid;
        var filesColumn = window.FindName("FilesColumn") as ColumnDefinition;
        var filesDividerColumn = window.FindName("FilesDividerColumn") as ColumnDefinition;
        var filesPanel = window.FindName("FilesPanel") as FrameworkElement;
        var filesToggle = window.FindName("FilesPanelToggleButton") as FrameworkElement;
        var shellGrid = filesPanel?.Parent as Grid;

        if (enabled)
        {
            if (!state.FocusSnapshotCaptured)
            {
                state.PreviousFilesWidth = filesColumn?.Width ?? new GridLength(300);
                state.PreviousFilesDividerWidth = filesDividerColumn?.Width ?? new GridLength(1);
                state.PreviousToggleColumnWidth = shellGrid is not null && shellGrid.ColumnDefinitions.Count > 2
                    ? shellGrid.ColumnDefinitions[2].Width
                    : new GridLength(24);
                state.PreviousHeaderHeight = rootGrid is not null && rootGrid.RowDefinitions.Count > 1
                    ? rootGrid.RowDefinitions[1].Height
                    : new GridLength(72);
                state.FocusSnapshotCaptured = true;
            }

            if (filesColumn is not null) filesColumn.Width = new GridLength(0);
            if (filesDividerColumn is not null) filesDividerColumn.Width = new GridLength(0);
            if (shellGrid is not null && shellGrid.ColumnDefinitions.Count > 2)
                shellGrid.ColumnDefinitions[2].Width = new GridLength(0);
            if (filesPanel is not null) filesPanel.Visibility = Visibility.Collapsed;
            if (filesToggle is not null) filesToggle.Visibility = Visibility.Collapsed;
            if (rootGrid is not null && rootGrid.RowDefinitions.Count > 1)
                rootGrid.RowDefinitions[1].Height = new GridLength(0);
            return;
        }

        if (filesColumn is not null)
            filesColumn.Width = state.FocusSnapshotCaptured ? state.PreviousFilesWidth : new GridLength(300);
        if (filesDividerColumn is not null)
            filesDividerColumn.Width = state.FocusSnapshotCaptured ? state.PreviousFilesDividerWidth : new GridLength(1);
        if (shellGrid is not null && shellGrid.ColumnDefinitions.Count > 2)
            shellGrid.ColumnDefinitions[2].Width = state.FocusSnapshotCaptured ? state.PreviousToggleColumnWidth : new GridLength(24);
        if (filesPanel is not null) filesPanel.Visibility = Visibility.Visible;
        if (filesToggle is not null) filesToggle.Visibility = Visibility.Visible;
        if (rootGrid is not null && rootGrid.RowDefinitions.Count > 1)
            rootGrid.RowDefinitions[1].Height = state.FocusSnapshotCaptured ? state.PreviousHeaderHeight : new GridLength(72);

        state.FocusSnapshotCaptured = false;
    }

    private static void InstallMenus(MainWindow window, HostState state)
    {
        var menu = FindVisualChild<Menu>(window);
        if (menu is null)
            return;

        var tools = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(x => string.Equals(x.Header?.ToString(), "Инструменты", StringComparison.Ordinal));

        if (tools is not null)
        {
            tools.Items.Add(new Separator());

            var glossary = new MenuItem { Header = "Глоссарий…" };
            glossary.Click += (_, _) =>
            {
                var dialog = new GlossaryWindow { Owner = window };
                if (dialog.ShowDialog() != true)
                    return;

                GlossaryService.Reload();
                state.ViewModel.SelectedEntry?.RefreshValidation();
                state.ViewModel.EntriesView.Refresh();
            };
            tools.Items.Add(glossary);

            var projectHistory = new MenuItem { Header = "История проекта…" };
            projectHistory.Click += (_, _) =>
            {
                state.Workspace.CommitTranslation();
                new ProjectHistoryWindow(state.ViewModel.ActiveDocument?.FilePath)
                {
                    Owner = window
                }.ShowDialog();
            };
            tools.Items.Add(projectHistory);

            var qaProfiles = new MenuItem { Header = "QA-профили…" };
            qaProfiles.Click += (_, _) =>
            {
                var dialog = new QaProfilesWindow { Owner = window };
                if (dialog.ShowDialog() != true || state.ViewModel.ActiveDocument is null)
                    return;

                state.ViewModel.SelectedEntry?.RefreshValidation();
                state.ViewModel.EntriesView.Refresh();
            };
            tools.Items.Add(qaProfiles);
        }

        var existingSettings = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(x => string.Equals(x.Header?.ToString(), "Настройки", StringComparison.Ordinal));
        if (existingSettings is null)
        {
            var settings = new MenuItem { Header = "Настройки" };
            settings.Click += (_, _) =>
            {
                var dialog = new EditorSettingsWindow { Owner = window };
                if (dialog.ShowDialog() == true)
                {
                    state.Workspace.ApplySettings();
                    ApplyFocusMode(window, state);
                }
            };

            var helpIndex = menu.Items.OfType<MenuItem>()
                .Select((item, index) => (item, index))
                .FirstOrDefault(x => string.Equals(x.item.Header?.ToString(), "Справка", StringComparison.Ordinal))
                .index;

            if (helpIndex > 0)
                menu.Items.Insert(helpIndex, settings);
            else
                menu.Items.Add(settings);
        }
    }

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

    private sealed record HostState(
        RowDefinition EditorRow,
        EditorWorkspaceControl Workspace,
        DataGrid EntriesGrid,
        MainViewModel ViewModel)
    {
        public bool FocusSnapshotCaptured { get; set; }
        public GridLength PreviousFilesWidth { get; set; } = new(300);
        public GridLength PreviousFilesDividerWidth { get; set; } = new(1);
        public GridLength PreviousToggleColumnWidth { get; set; } = new(24);
        public GridLength PreviousHeaderHeight { get; set; } = new(72);
    }
}
