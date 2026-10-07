using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage : UserControl
{
    private MainViewModel? _viewModel;
    private LocalizationEntry? _currentEntry;

    public TranslationPage()
    {
        InitializeComponent();
        Unloaded += TranslationPage_Unloaded;
    }

    public void Attach(MainViewModel viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RefreshSelection();
    }

    public void CommitTranslation()
    {
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    public void ScrollToSelected()
    {
        if (_viewModel?.SelectedEntry is null)
            return;

        EntriesGrid.UpdateLayout();
        EntriesGrid.SelectedItem = _viewModel.SelectedEntry;
        EntriesGrid.ScrollIntoView(_viewModel.SelectedEntry);
    }

    public void NavigateQaError(int direction)
    {
        if (_viewModel is null)
            return;

        if (_viewModel.FilterAllCommand.CanExecute(null))
            _viewModel.FilterAllCommand.Execute(null);

        _viewModel.SearchText = string.Empty;
        var errors = _viewModel.GetErrorEntries().ToList();
        if (errors.Count == 0)
        {
            AppDialog.Show(
                "QA-ошибок в текущем файле нет.",
                "QA",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                Window.GetWindow(this));
            return;
        }

        var currentIndex = _viewModel.SelectedEntry is null
            ? -1
            : errors.IndexOf(_viewModel.SelectedEntry);
        var nextIndex = direction > 0
            ? (currentIndex + 1 + errors.Count) % errors.Count
            : (currentIndex <= 0 ? errors.Count - 1 : currentIndex - 1);

        _viewModel.SelectedEntry = errors[nextIndex];
        ScrollToSelected();
    }

    private void TranslationPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEntry))
            RefreshSelection();

        if (e.PropertyName is nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.TranslatedCount)
            or nameof(MainViewModel.UntranslatedCount)
            or nameof(MainViewModel.ModifiedCount))
        {
            RefreshFileInfo();
        }
    }

    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        _currentEntry = _viewModel?.SelectedEntry;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

        EntryMetaText.Text = _currentEntry is null
            ? string.Empty
            : $"Namespace: {_currentEntry.Namespace}  ·  ID: {_currentEntry.Index:N0}";

        RefreshFileInfo();
        RefreshCharacterCount();
        RefreshTranslationBorder();
    }

    private void RefreshFileInfo()
    {
        var document = _viewModel?.ActiveDocument;
        FileInfoText.Text = document is null
            ? string.Empty
            : Path.GetFileName(document.FilePath);

        if (document is null || _currentEntry is null)
        {
            PositionText.Text = string.Empty;
            return;
        }

        var position = document.Entries.IndexOf(_currentEntry);
        PositionText.Text = position >= 0
            ? $"{position + 1:N0} / {document.Entries.Count:N0}"
            : $"{document.Entries.Count:N0} строк";
    }

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshCharacterCount();
        RefreshTranslationBorder();
    }

    private void TranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitTranslation();
        RefreshTranslationBorder();
    }

    private void RefreshCharacterCount()
    {
        CharacterCountText.Text = $"{TranslationBox.Text.Length:N0} символов";
    }

    private void RefreshTranslationBorder()
    {
        if (_currentEntry is null)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239));
            return;
        }

        if (string.IsNullOrWhiteSpace(TranslationBox.Text))
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239));
            return;
        }

        if (_currentEntry.HasStructuralValidationIssues)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 76, 85));
            return;
        }

        if (_currentEntry.HasValidationIssues)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 154, 36));
            return;
        }

        TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 135, 238));
    }

    private void CopyOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentEntry?.Original))
            Clipboard.SetText(_currentEntry.Original);
    }
}
