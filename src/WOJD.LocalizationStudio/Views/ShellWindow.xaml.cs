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
    private Button? _activeNavButton;

    public ShellWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        TranslationPage.Attach(_viewModel);
        ProjectPage.Attach(_viewModel);
        SearchPage.Attach(_viewModel);
        QaPage.Attach(_viewModel);
        StatisticsPage.Attach(_viewModel);

        SearchPage.OpenInEditorRequested += (_, _) =>
        {
            ShowTranslation();
            TranslationPage.ScrollToSelected();
        };
        QaPage.OpenInEditorRequested += (_, _) =>
        {
            ShowTranslation();
            TranslationPage.ScrollToSelected();
        };
        SettingsPage.SettingsSaved += (_, _) => TranslationPage.ApplySettings();

        Loaded += ShellWindow_Loaded;
        Closing += ShellWindow_Closing;
        PreviewKeyDown += ShellWindow_PreviewKeyDown;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshTopBar();
        SetActiveNavigation(TranslationNavButton);
    }

    private async void ShellWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ShellWindow_Loaded;
        await _viewModel.RestoreWorkspaceAsync();
        ProjectPage.Refresh();
        QaPage.Refresh();
        StatisticsPage.Refresh();
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
        => ShowTranslation();

    private void ShowTranslation()
    {
        ShowPage(TranslationPage, "Перевод", TranslationNavButton);
        TranslationPage.Attach(_viewModel);
        RefreshTopBar();
    }

    private void Project_Click(object sender, RoutedEventArgs e)
    {
        TranslationPage.CommitTranslation();
        ProjectPage.Refresh();
        ShowPage(ProjectPage, "Проект", ProjectNavButton);
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        TranslationPage.CommitTranslation();
        ShowPage(SearchPage, "Поиск", SearchNavButton);
        SearchPage.FocusSearch();
    }

    private void Qa_Click(object sender, RoutedEventArgs e)
    {
        TranslationPage.CommitTranslation();
        QaPage.Refresh();
        ShowPage(QaPage, "Проверка", QaNavButton);
    }

    private void Statistics_Click(object sender, RoutedEventArgs e)
    {
        TranslationPage.CommitTranslation();
        StatisticsPage.Refresh();
        ShowPage(StatisticsPage, "Статистика", StatisticsNavButton);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        TranslationPage.CommitTranslation();
        SettingsPage.Reload();
        ShowPage(SettingsPage, "Настройки", SettingsNavButton);
    }

    private void ShowPage(UIElement page, string title, Button navButton)
    {
        TranslationPage.Visibility = Visibility.Collapsed;
        ProjectPage.Visibility = Visibility.Collapsed;
        SearchPage.Visibility = Visibility.Collapsed;
        QaPage.Visibility = Visibility.Collapsed;
        StatisticsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        SectionTitleText.Text = title;
        SetActiveNavigation(navButton);
        RefreshTopBar();
    }

    private void SetActiveNavigation(Button active)
    {
        _activeNavButton = active;
        foreach (var button in new[]
                 {
                     ProjectNavButton,
                     TranslationNavButton,
                     SearchNavButton,
                     QaNavButton,
                     StatisticsNavButton,
                     SettingsNavButton,
                     AboutNavButton
                 })
        {
            button.Style = (Style)FindResource(
                ReferenceEquals(button, active)
                    ? "NavButtonActive"
                    : "NavButton");
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var previous = _activeNavButton;
        SetActiveNavigation(AboutNavButton);
        AppDialog.Show(
            $"WOJD Localization Studio\n{_viewModel.AppVersion}\n\nРедизайн 0.2.0 — единая оболочка перевода, поиска, QA, статистики и настроек.",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            this);
        if (previous is not null)
            SetActiveNavigation(previous);
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

        if (ctrl && shift && e.Key == Key.F)
        {
            ShowPage(SearchPage, "Поиск", SearchNavButton);
            SearchPage.FocusSearch();
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
            ShowTranslation();
            TranslationPage.ScrollToSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7 && !ctrl)
        {
            TranslationPage.CommitTranslation();
            TranslationPage.NavigateQaError(shift ? -1 : 1);
            ShowTranslation();
            e.Handled = true;
        }
    }

    private void ShellWindow_Closing(object? sender, CancelEventArgs e)
    {
        TranslationPage.CommitTranslation();

        if (UpdateService.IsApplyingUpdate)
        {
            _viewModel.PersistWorkspaceState(includeDrafts: true);
            DisposePages();
            return;
        }

        if (!_viewModel.ConfirmDiscardUnsaved())
        {
            e.Cancel = true;
            return;
        }

        _viewModel.PersistWorkspaceState(includeDrafts: false);
        DisposePages();
    }

    private void DisposePages()
    {
        TranslationPage.DisposePage();
        ProjectPage.Detach();
        QaPage.Detach();
        StatisticsPage.Detach();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
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
