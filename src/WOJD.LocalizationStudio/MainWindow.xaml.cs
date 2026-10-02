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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;

        ApplyWindowSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        await _viewModel.RestoreWorkspaceAsync();
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

    private void ProjectTools_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new ProjectToolsWindow(_viewModel)
            {
                Owner = this
            };

        dialog.ShowDialog();
    }

    private void ProjectReplace_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new ProjectReplaceWindow(_viewModel)
            {
                Owner = this
            };

        dialog.ShowDialog();
    }

    private void ExpandedEditor_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.SelectedEntry is null)
            return;

        var dialog =
            new ExpandedEditorWindow(
                _viewModel.SelectedEntry)
            {
                Owner = this
            };

        dialog.ShowDialog();
    }

    private void Settings_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new SettingsWindow
            {
                Owner = this
            };

        if (dialog.ShowDialog() != true)
            return;

        ApplyWindowSettings();
    }

    private void NamespaceStat_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not NamespaceStat stat)
        {
            return;
        }

        _viewModel.FilterToNamespace(
            stat.Namespace);
    }

    private void OpenTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FileNode node)
        {
            return;
        }

        if (_viewModel.SwitchFileCommand.CanExecute(node))
            _viewModel.SwitchFileCommand.Execute(node);
    }

    private void OpenTab_PreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle ||
            sender is not FrameworkElement element ||
            element.DataContext is not FileNode node)
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.CloseFileCommand.CanExecute(node))
            _viewModel.CloseFileCommand.Execute(node);
    }

    private void PinTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FileNode node)
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.PinFileCommand.CanExecute(node))
            _viewModel.PinFileCommand.Execute(node);
    }

    private void OpenTabClose_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FileNode node)
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.CloseFileCommand.CanExecute(node))
            _viewModel.CloseFileCommand.Execute(node);
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
        if (UpdateService.IsApplyingUpdate)
        {
            SaveWindowSettings();
            _viewModel.PersistWorkspaceState(includeDrafts: true);
            return;
        }

        if (!_viewModel.ConfirmDiscardUnsaved())
        {
            e.Cancel = true;
            return;
        }

        SaveWindowSettings();
        _viewModel.PersistWorkspaceState(includeDrafts: false);
    }

    private void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (MatchesConfiguredHotkey(
                e,
                "ProjectSearch",
                "Ctrl+Shift+F"))
        {
            ShowProjectSearchWindow();
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredHotkey(
                e,
                "NextUntranslated",
                "F6"))
        {
            if (_viewModel.NextUntranslatedCommand.CanExecute(null))
                _viewModel.NextUntranslatedCommand.Execute(null);

            e.Handled = true;
            return;
        }

        if (MatchesConfiguredHotkey(
                e,
                "PreviousUntranslated",
                "Shift+F6"))
        {
            if (_viewModel.PreviousUntranslatedCommand.CanExecute(null))
                _viewModel.PreviousUntranslatedCommand.Execute(null);

            e.Handled = true;
            return;
        }

        if (MatchesConfiguredHotkey(
                e,
                "SaveAll",
                "Ctrl+Shift+S"))
        {
            if (_viewModel.SaveAllCommand.CanExecute(null))
                _viewModel.SaveAllCommand.Execute(null);

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

        if (e.Key == Key.G &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGoToDialog();
            e.Handled = true;
        }
    }

    private static bool MatchesConfiguredHotkey(
        KeyEventArgs e,
        string action,
        string fallback)
    {
        var text =
            AppSettingsService.Current.Hotkeys
                .TryGetValue(action, out var configured) &&
            !string.IsNullOrWhiteSpace(configured)
                ? configured
                : fallback;

        var parts =
            text.Split(
                '+',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return false;

        var modifiers =
            ModifierKeys.None;

        Key? key = null;

        foreach (var part in parts)
        {
            if (part.Equals(
                    "Ctrl",
                    StringComparison.OrdinalIgnoreCase) ||
                part.Equals(
                    "Control",
                    StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
                continue;
            }

            if (part.Equals(
                    "Shift",
                    StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
                continue;
            }

            if (part.Equals(
                    "Alt",
                    StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
                continue;
            }

            if (part.Equals(
                    "Win",
                    StringComparison.OrdinalIgnoreCase) ||
                part.Equals(
                    "Windows",
                    StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
                continue;
            }

            if (Enum.TryParse<Key>(
                    part,
                    ignoreCase: true,
                    out var parsedKey))
            {
                key = parsedKey;
            }
        }

        return key.HasValue &&
               e.Key == key.Value &&
               Keyboard.Modifiers == modifiers;
    }

    private void ToggleFilesPanel_Click(object sender, RoutedEventArgs e)
    {
        _filesPanelVisible = !_filesPanelVisible;

        FilesPanel.Visibility = _filesPanelVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilesColumn.Width = _filesPanelVisible
            ? new GridLength(300)
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

    private void ApplyWindowSettings()
    {
        var settings =
            AppSettingsService.Current;

        FontSize =
            settings.FontSize;

        Width =
            Math.Max(
                MinWidth,
                settings.WindowWidth);

        Height =
            Math.Max(
                MinHeight,
                settings.WindowHeight);

        if (settings.WindowLeft.HasValue &&
            settings.WindowTop.HasValue)
        {
            Left =
                settings.WindowLeft.Value;

            Top =
                settings.WindowTop.Value;

            WindowStartupLocation =
                WindowStartupLocation.Manual;
        }

        FilesColumn.Width =
            new GridLength(
                Math.Clamp(
                    settings.FilesPanelWidth,
                    180,
                    700));

        _filesPanelVisible =
            !settings.FilesPanelCollapsed;

        FilesPanel.Visibility =
            _filesPanelVisible
                ? Visibility.Visible
                : Visibility.Collapsed;

        FilesDividerColumn.Width =
            _filesPanelVisible
                ? new GridLength(1)
                : new GridLength(0);

        if (!_filesPanelVisible)
            FilesColumn.Width = new GridLength(0);

        FilesPanelToggleButton.Content =
            _filesPanelVisible ? "‹" : "›";

        FilesPanelToggleButton.ToolTip =
            _filesPanelVisible
                ? "Скрыть панель файлов"
                : "Показать панель файлов";

        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowSettings()
    {
        var settings =
            AppSettingsService.Current;

        var bounds =
            WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

        settings.WindowWidth =
            bounds.Width;

        settings.WindowHeight =
            bounds.Height;

        settings.WindowLeft =
            bounds.Left;

        settings.WindowTop =
            bounds.Top;

        settings.WindowMaximized =
            WindowState == WindowState.Maximized;

        if (_filesPanelVisible &&
            FilesColumn.Width.Value > 0)
        {
            settings.FilesPanelWidth =
                FilesColumn.Width.Value;
        }

        settings.FilesPanelCollapsed =
            !_filesPanelVisible;

        AppSettingsService.Save(settings);
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
