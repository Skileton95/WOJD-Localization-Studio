using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class ShellWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public ShellWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        TranslationPage.Attach(_viewModel);
        Loaded += ShellWindow_Loaded;
        Closing += ShellWindow_Closing;
        PreviewKeyDown += ShellWindow_PreviewKeyDown;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshTopBar();
    }

    private async void ShellWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ShellWindow_Loaded;
        await _viewModel.RestoreWorkspaceAsync();
        RefreshTopBar();
        await UpdateService.StartAsync(
            this,
            SetUpdateProgress,
            _viewModel.ConfirmDiscardUnsaved);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedEntry)
            or nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.ModifiedCount))
        {
            RefreshTopBar();
        }
    }

    private void RefreshTopBar()
    {
        var document = _viewModel.ActiveDocument;
        var fileName = document is null ? string.Empty : Path.GetFileName(document.FilePath);
        ActiveFileText.Text = string.IsNullOrEmpty(fileName) ? "Файл не открыт" : fileName;
        TopFileText.Text = fileName;
        TopCountText.Text = document is null ? string.Empty : $"{document.Entries.Count:N0} строк";
    }

    private void Translation_Click(object sender, RoutedEventArgs e)
    {
        ShowTranslation();
    }

    private void ShowTranslation()
    {
        SectionTitleText.Text = "Перевод";
        TranslationPage.Visibility = Visibility.Visible;
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        RefreshTopBar();
    }

    private void Project_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var menu = new ContextMenu();
        var openFile = new MenuItem { Header = "Открыть файл…", InputGestureText = "Ctrl+O" };
        openFile.Click += (_, _) => _viewModel.OpenFileCommand.Execute(null);
        var openFolder = new MenuItem { Header = "Открыть папку…", InputGestureText = "Ctrl+Shift+O" };
        openFolder.Click += (_, _) => _viewModel.OpenFolderCommand.Execute(null);
        var save = new MenuItem { Header = "Сохранить", InputGestureText = "Ctrl+S" };
        save.Click += (_, _) => SaveCurrent();
        var saveAll = new MenuItem { Header = "Сохранить всё", InputGestureText = "Ctrl+Shift+S" };
        saveAll.Click += (_, _) => SaveAll();
        menu.Items.Add(openFile);
        menu.Items.Add(openFolder);
        menu.Items.Add(new Separator());
        menu.Items.Add(save);
        menu.Items.Add(saveAll);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectSearchWindow(_viewModel) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ShowTranslation();
            TranslationPage.ScrollToSelected();
        }
    }

    private void Qa_Click(object sender, RoutedEventArgs e)
    {
        ShowPlaceholder(
            "Проверка",
            "Новый экран проверки будет следующим этапом 0.2.0. Сейчас QA продолжает работать в таблице и карточке выбранной строки; F7 переходит к следующей проблеме.");
    }

    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        ShowPlaceholder(
            "Статистика",
            "Раздел статистики подготовлен в новой навигации. На следующем этапе здесь появятся прогресс перевода, QA, Namespace и динамика изменений.");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditorSettingsWindow { Owner = this };
        dialog.ShowDialog();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        AppDialog.Show(
            $"WOJD Localization Studio\n{_viewModel.AppVersion}\n\nРедизайн 0.2.0 — новая оболочка находится в активной разработке.",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            this);
    }

    private void ShowPlaceholder(string title, string description)
    {
        SectionTitleText.Text = title;
        TranslationPage.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = title;
        PlaceholderDescription.Text = description;
    }

    private void SaveCurrent()
    {
        TranslationPage.CommitTranslation();
        if (_viewModel.SaveCommand.CanExecute(null))
            _viewModel.SaveCommand.Execute(null);
    }

    private void SaveAll()
    {
        TranslationPage.CommitTranslation();
        if (_viewModel.SaveAllCommand.CanExecute(null))
            _viewModel.SaveAllCommand.Execute(null);
    }

    private void ShellWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl && !shift && e.Key == Key.O)
        {
            _viewModel.OpenFileCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.O)
        {
            _viewModel.OpenFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == Key.S)
        {
            SaveCurrent();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.S)
        {
            SaveAll();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == Key.F)
        {
            ShowTranslation();
            TranslationPage.FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && !ctrl)
        {
            TranslationPage.CommitTranslation();
            var command = shift ? _viewModel.PreviousUntranslatedCommand : _viewModel.NextUntranslatedCommand;
            if (command.CanExecute(null))
                command.Execute(null);
            TranslationPage.ScrollToSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7 && !ctrl)
        {
            TranslationPage.CommitTranslation();
            TranslationPage.NavigateQaError(shift ? -1 : 1);
            e.Handled = true;
        }
    }

    private void ShellWindow_Closing(object? sender, CancelEventArgs e)
    {
        TranslationPage.CommitTranslation();

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
