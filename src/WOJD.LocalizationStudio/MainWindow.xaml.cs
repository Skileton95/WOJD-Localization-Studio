using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _filesPanelVisible = true;
    private double _filesPanelWidth = 300;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        await _viewModel.RestoreWorkspaceAsync();
        RestoreLayout();
        await UpdateService.StartAsync(
            this,
            SetUpdateProgress,
            _viewModel.ConfirmDiscardUnsaved);
    }

    private async void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileNode { IsDirectory: false } node)
            await _viewModel.LoadPathAsync(node.FullPath);
    }

    private void CloseFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not FrameworkElement placementTarget ||
            placementTarget.DataContext is not FileNode node ||
            node.IsDirectory)
        {
            return;
        }

        if (_viewModel.CloseFileCommand.CanExecute(node))
            _viewModel.CloseFileCommand.Execute(node);
    }

    private void CloseFileTreeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FileNode node ||
            node.IsDirectory)
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.CloseFileCommand.CanExecute(node))
            _viewModel.CloseFileCommand.Execute(node);
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        ShowGoToDialog();
    }

    private void ShowGoToDialog()
    {
        var dialog = new TextInputDialog(
            "Перейти",
            "Введите номер строки, ключ или Namespace:Key.")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        _viewModel.GoTo(dialog.Value);

        if (_viewModel.SelectedEntry is not null)
        {
            EntriesGrid.UpdateLayout();
            EntriesGrid.ScrollIntoView(_viewModel.SelectedEntry);
        }
    }

    private void EntriesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not null)
            EntriesGrid.ScrollIntoView(EntriesGrid.SelectedItem);
    }

    private List<LocalizationEntry> GetSelectedEntries()
        => EntriesGrid.SelectedItems
            .OfType<LocalizationEntry>()
            .ToList();

    private void CopyOriginalToTranslation_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entries = GetSelectedEntries();

        if (entries.Count == 0 &&
            _viewModel.SelectedEntry is not null)
        {
            entries.Add(_viewModel.SelectedEntry);
        }

        _viewModel.CopyOriginalToTranslation(entries);
    }

    private void ClearSelectedTranslations_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entries = GetSelectedEntries();

        if (entries.Count == 0 &&
            _viewModel.SelectedEntry is not null)
        {
            entries.Add(_viewModel.SelectedEntry);
        }

        if (entries.Count == 0)
            return;

        var result =
            AppDialog.Show(
                $"Очистить перевод у строк: {entries.Count}?",
                "Массовое действие",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                this);

        if (result == MessageBoxResult.Yes)
            _viewModel.ClearTranslations(entries);
    }

    private async void ExportSelected_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.ExportEntriesAsync(
            GetSelectedEntries(),
            "selected");
    }

    private async void ExportUntranslated_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.ExportEntriesAsync(
            _viewModel.GetUntranslatedEntries(),
            "untranslated");
    }

    private async void ExportErrors_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.ExportEntriesAsync(
            _viewModel.GetErrorEntries(),
            "qa-errors");
    }

    private void ProjectSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowProjectSearchWindow();
    }

    private void ShowProjectSearchWindow()
    {
        var dialog =
            new ProjectSearchWindow(_viewModel)
            {
                Owner = this
            };

        var navigated =
            dialog.ShowDialog() == true;

        if (!navigated ||
            _viewModel.SelectedEntry is null)
        {
            return;
        }

        EntriesGrid.UpdateLayout();
        EntriesGrid.ScrollIntoView(
            _viewModel.SelectedEntry);
        EntriesGrid.SelectedItem =
            _viewModel.SelectedEntry;
        EntriesGrid.Focus();
    }

    private async void CompareFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        var current =
            _viewModel.ActiveDocument;

        if (current is null)
        {
            AppDialog.Show(
                "Сначала откройте текущую версию файла.",
                "Сравнение файлов",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);

            return;
        }

        var dialog =
            new OpenFileDialog
            {
                Title = "Выберите старую версию файла",
                Filter =
                    "NDJSON/JSONL (*.ndjson;*.jsonl)|*.ndjson;*.jsonl|Все файлы (*.*)|*.*"
            };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var oldDocument =
                await new NdjsonLocalizationAdapter()
                    .LoadAsync(dialog.FileName);

            var compareWindow =
                new FileComparisonWindow(
                    current,
                    oldDocument,
                    System.IO.Path.GetFileName(dialog.FileName))
                {
                    Owner = this
                };

            compareWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка сравнения",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                this);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await UpdateService.CheckNowAsync(
            this,
            SetUpdateProgress,
            _viewModel.ConfirmDiscardUnsaved);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        CaptureLayout();
        if (UpdateService.IsApplyingUpdate)
        {
            _viewModel.PersistWorkspaceState(includeDrafts: true);
            return;
        }

        if (!_viewModel.ConfirmDiscardUnsaved())
        {
            e.Cancel = true;
            return;
        }

        _viewModel.PersistWorkspaceState(includeDrafts: false);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.Control ||
            Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            _viewModel.CycleTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = _viewModel.CloseCurrentFileAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F &&
            Keyboard.Modifiers ==
                (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowProjectSearchWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.G &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGoToDialog();
            e.Handled = true;
        }
    }

    private void ToggleFilesPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_filesPanelVisible) _filesPanelWidth = FilesColumn.ActualWidth;
        _filesPanelVisible = !_filesPanelVisible;

        ApplyFilesLayout();
    }

    private void ApplyFilesLayout()
    {

        FilesPanel.Visibility = _filesPanelVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilesColumn.Width = _filesPanelVisible
            ? new GridLength(Math.Clamp(_filesPanelWidth, 220, 600))
            : new GridLength(0);

        FilesDividerColumn.Width = new GridLength(24);
        FilesSplitter.Visibility = _filesPanelVisible ? Visibility.Visible : Visibility.Collapsed;

        FilesPanelToggleButton.Content =
            _filesPanelVisible ? "‹" : "›";

        FilesPanelToggleButton.ToolTip =
            _filesPanelVisible
                ? "Скрыть панель файлов"
                : "Показать панель файлов";
    }

    private async void OpenTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileNode node } && !_viewModel.IsBusy)
            await _viewModel.LoadPathAsync(node.FullPath);
    }

    private void PinTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileNode node }) _viewModel.TogglePin(node);
    }

    private void Tab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _viewModel.IsBusy) return;
        if (sender is FrameworkElement { DataContext: FileNode node })
            _viewModel.CloseFileCommand.Execute(node);
        e.Handled = true;
    }

    private void FilesSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _filesPanelWidth = Math.Clamp(FilesColumn.ActualWidth, 220, 600);
        ApplyFilesLayout();
    }

    private void CaptureLayout()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        _viewModel.WindowLayout = new WindowLayoutState(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized, _filesPanelVisible ? FilesColumn.ActualWidth : _filesPanelWidth, _filesPanelVisible);
    }

    private void RestoreLayout()
    {
        if (_viewModel.WindowLayout is not { } layout) return;
        if (double.IsFinite(layout.Width) && double.IsFinite(layout.Height) &&
            double.IsFinite(layout.Left) && double.IsFinite(layout.Top))
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Clamp(layout.Width, MinWidth, Math.Max(MinWidth, area.Width));
            Height = Math.Clamp(layout.Height, MinHeight, Math.Max(MinHeight, area.Height));
            Left = Math.Clamp(layout.Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(layout.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            if (layout.Maximized) WindowState = WindowState.Maximized;
        }
        _filesPanelWidth = double.IsFinite(layout.FilesWidth) ? Math.Clamp(layout.FilesWidth, 220, 600) : 300;
        _filesPanelVisible = layout.FilesVisible;
        ApplyFilesLayout();
    }

    private void NamespaceSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NamespaceSummary summary }) _viewModel.SelectNamespace(summary.Name);
    }
    private void NamespaceReset_Click(object sender, RoutedEventArgs e) => _viewModel.ClearNamespaceFilterCommand.Execute(null);
    private async void NamespaceExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NamespaceSummary summary })
            await _viewModel.ExportEntriesAsync(_viewModel.Entries.Where(x => x.Namespace == summary.Name).ToList(), "namespace");
    }

    private void Consistency_Click(object sender, RoutedEventArgs e) => new ConsistencyWindow(_viewModel) { Owner = this }.ShowDialog();

    private void ProjectReplace_Click(object sender, RoutedEventArgs e) => new ProjectReplaceWindow(_viewModel) { Owner = this }.ShowDialog();

    private void PatchSync_Click(object sender, RoutedEventArgs e) => new PatchSyncWindow(_viewModel) { Owner = this }.ShowDialog();

    private void SetUpdateProgress(UpdateProgressState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetUpdateProgress(state));
            return;
        }

        UpdatePanel.Visibility = state.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusText.Text = state.Status;
        UpdateProgressBar.IsIndeterminate = state.IsIndeterminate;
        UpdateProgressBar.Value = state.Progress;

        UpdatePercentText.Text = state.IsIndeterminate
            ? string.Empty
            : $"{Math.Clamp((int)Math.Round(state.Progress), 0, 100)}%";
    }
}
