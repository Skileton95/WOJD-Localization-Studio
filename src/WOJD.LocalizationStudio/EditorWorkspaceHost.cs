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

/// <summary>
/// One small bridge from the legacy MainWindow XAML to the new static editor
/// UserControl. All editor buttons and panels live in EditorWorkspaceControl.xaml;
/// this host only replaces the legacy lower region and owns the splitter row.
/// </summary>
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

        // Mount once after InitializeComponent, before the user starts editing.
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

        // The legacy editor occupied the last row. Reuse it as a dedicated
        // splitter row and append a new editor row. Nothing else in MainWindow
        // competes for that space anymore.
        if (legacyRow < 0 || legacyRow >= contentGrid.RowDefinitions.Count)
            return;

        contentGrid.RowDefinitions[legacyRow].Height = new GridLength(6);
        var editorRow = new RowDefinition();
        contentGrid.RowDefinitions.Add(editorRow);
        var editorRowIndex = contentGrid.RowDefinitions.Count - 1;

        var splitter = new GridSplitter
        {
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNS,
            ToolTip = "Изменить высоту редактора"
        };
        Grid.SetRow(splitter, legacyRow);
        contentGrid.Children.Add(splitter);

        var workspace = new EditorWorkspaceControl
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(workspace, editorRowIndex);
        contentGrid.Children.Add(workspace);

        var state = new HostState(
            contentGrid,
            editorRow,
            splitter,
            workspace,
            entriesGrid,
            viewModel);
        States.Add(window, state);

        workspace.Attach(window, viewModel, entriesGrid);
        workspace.CollapseRequested += (_, _) => ApplyLayout(state);
        workspace.SettingsChanged += (_, _) => ApplyLayout(state);
        splitter.DragCompleted += (_, _) =>
        {
            if (!workspace.IsCollapsed)
                workspace.SetExpandedHeight(editorRow.ActualHeight);
        };

        window.PreviewKeyDown += (_, args) => HandlePreviewKeyDown(state, args);
        window.Closed += (_, _) => workspace.CommitTranslation();

        InstallToolsMenu(window, state);
        ApplyLayout(state);
    }

    private static void ApplyLayout(HostState state)
    {
        var settings = EditorSettingsService.Current;
        state.EditorRow.Height = new GridLength(state.Workspace.DesiredEditorHeight);
        state.Splitter.Visibility = state.Workspace.IsCollapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        state.ContentGrid.RowDefinitions[Grid.GetRow(state.Splitter)].Height =
            state.Workspace.IsCollapsed ? new GridLength(0) : new GridLength(6);

        SetColumnVisibility(state.EntriesGrid, "Namespace", settings.ShowNamespaceColumn);
        SetColumnVisibility(state.EntriesGrid, "Оригинал", settings.ShowOriginalColumn);
        SetColumnVisibility(state.EntriesGrid, "Статус", settings.ShowStatusColumn);
    }

    private static void SetColumnVisibility(
        DataGrid grid,
        string header,
        bool visible)
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

    private static void InstallToolsMenu(MainWindow window, HostState state)
    {
        var menu = FindVisualChild<Menu>(window);
        var tools = menu?.Items.OfType<MenuItem>()
            .FirstOrDefault(x => string.Equals(x.Header?.ToString(), "Инструменты", StringComparison.Ordinal));
        if (tools is null)
            return;

        tools.Items.Add(new Separator());

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

            // Current row updates immediately. A full-file QA refresh is exposed
            // below as a cancellable background command instead of freezing UI.
            state.ViewModel.SelectedEntry?.RefreshValidation();
            state.ViewModel.EntriesView.Refresh();
        };
        tools.Items.Add(qaProfiles);

        var aiSettings = new MenuItem { Header = "Настройка ИИ…" };
        aiSettings.Click += (_, _) =>
        {
            var dialog = new AiSettingsWindow { Owner = window };
            if (dialog.ShowDialog() == true)
                AiCorrectionService.ConfigureSession(dialog.ApiKey, dialog.Model);
        };
        tools.Items.Add(aiSettings);
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
        Grid ContentGrid,
        RowDefinition EditorRow,
        GridSplitter Splitter,
        EditorWorkspaceControl Workspace,
        DataGrid EntriesGrid,
        MainViewModel ViewModel);
}
