using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _filesPanelVisible = true;

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
        await UpdateService.StartAsync(this, SetUpdateProgress, _viewModel.ConfirmDiscardUnsaved);
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await UpdateService.CheckNowAsync(
            this,
            SetUpdateProgress,
            _viewModel.ConfirmDiscardUnsaved);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!UpdateService.IsApplyingUpdate && !_viewModel.ConfirmDiscardUnsaved())
            e.Cancel = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.G &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowGoToDialog();
            e.Handled = true;
        }
    }

    private void ToggleFilesPanel_Click(object sender, RoutedEventArgs e)
    {
        _filesPanelVisible = !_filesPanelVisible;

        FilesPanel.Visibility = _filesPanelVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilesColumn.Width = _filesPanelVisible
            ? new GridLength(220)
            : new GridLength(0);

        FilesDividerColumn.Width = _filesPanelVisible
            ? new GridLength(1)
            : new GridLength(0);

        FilesPanelToggleButton.Content =
            _filesPanelVisible ? "‹" : "›";

        FilesPanelToggleButton.ToolTip =
            _filesPanelVisible
                ? "Скрыть панель файлов"
                : "Показать панель файлов";
    }

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
