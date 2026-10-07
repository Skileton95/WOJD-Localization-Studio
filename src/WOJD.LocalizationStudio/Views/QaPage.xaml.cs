using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class QaPage : UserControl
{
    private MainViewModel? _viewModel;
    private readonly ObservableCollection<QaCategory> _categories = [];
    private readonly ObservableCollection<LocalizationEntry> _issues = [];
    private LocalizationEntry? _selectedIssue;

    public event EventHandler? OpenInEditorRequested;

    public QaPage()
    {
        InitializeComponent();
        CategoryList.ItemsSource = _categories;
        IssueGrid.ItemsSource = _issues;
    }

    public void Attach(MainViewModel viewModel)
    {
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        Refresh();
    }

    public void Detach()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    public void Refresh()
    {
        var document = _viewModel?.ActiveDocument;
        var entries = document?.Entries ?? [];
        var currentId = (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";

        _categories.Clear();
        _categories.Add(new QaCategory("all", "Все проблемы", entries.Count(IsAnyProblem)));
        _categories.Add(new QaCategory("untranslated", "Отсутствующие переводы", entries.Count(x => string.IsNullOrWhiteSpace(x.Translation))));
        _categories.Add(new QaCategory("structure", "Теги и плейсхолдеры", entries.Count(x => x.HasTagIssues || x.HasPlaceholderIssues)));
        _categories.Add(new QaCategory("newlines", "Переносы строк", entries.Count(x => x.HasNewLineIssues)));
        _categories.Add(new QaCategory("glossary", "Глоссарий", entries.Count(x => x.HasGlossaryIssue)));
        _categories.Add(new QaCategory("potential", "Потенциальные ошибки", entries.Count(IsPotentialProblem)));

        CategoryList.SelectedItem = _categories.FirstOrDefault(x => x.Id == currentId) ?? _categories.FirstOrDefault();
        RefreshIssues();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.TranslatedCount)
            or nameof(MainViewModel.UntranslatedCount)
            or nameof(MainViewModel.ErrorCount)
            or nameof(MainViewModel.ModifiedCount))
        {
            Refresh();
        }
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshIssues();

    private void RefreshIssues()
    {
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
        {
            _issues.Clear();
            IssueTitleText.Text = "Проверка";
            IssueCountText.Text = "Файл не открыт";
            ShowDetails(null);
            return;
        }

        var category = CategoryList.SelectedItem as QaCategory;
        var id = category?.Id ?? "all";
        var filtered = document.Entries.Where(x => Matches(id, x)).ToList();

        _issues.Clear();
        foreach (var item in filtered)
            _issues.Add(item);

        IssueTitleText.Text = category?.Name ?? "Все проблемы";
        IssueCountText.Text = $"{filtered.Count:N0}";
        IssueGrid.SelectedItem = filtered.FirstOrDefault();
        ShowDetails(IssueGrid.SelectedItem as LocalizationEntry);
    }

    private static bool Matches(string id, LocalizationEntry entry)
        => id switch
        {
            "untranslated" => string.IsNullOrWhiteSpace(entry.Translation),
            "structure" => entry.HasTagIssues || entry.HasPlaceholderIssues,
            "newlines" => entry.HasNewLineIssues,
            "glossary" => entry.HasGlossaryIssue,
            "potential" => IsPotentialProblem(entry),
            _ => IsAnyProblem(entry)
        };

    private static bool IsAnyProblem(LocalizationEntry entry)
        => string.IsNullOrWhiteSpace(entry.Translation) || entry.HasValidationIssues;

    private static bool IsPotentialProblem(LocalizationEntry entry)
        => entry.HasSameAsSourceIssue
           || entry.HasSuspiciousLengthIssue
           || entry.HasProfileRuleIssue
           || entry.HasSourceMissingIssue;

    private void IssueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowDetails(IssueGrid.SelectedItem as LocalizationEntry);

    private void IssueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueGrid.SelectedItem is LocalizationEntry)
            OpenSelected();
    }

    private void ShowDetails(LocalizationEntry? entry)
    {
        _selectedIssue = entry;
        OpenInEditorButton.IsEnabled = entry is not null;

        if (entry is null)
        {
            DetailStateText.Text = "Выберите проблему";
            DetailOriginalText.Text = string.Empty;
            DetailKeyText.Text = string.Empty;
            DetailReasonText.Text = string.Empty;
            DetailTranslationText.Text = string.Empty;
            return;
        }

        DetailStateText.Text = string.IsNullOrWhiteSpace(entry.Translation)
            ? "Отсутствует перевод"
            : entry.QaStateText;
        DetailOriginalText.Text = entry.OriginalDisplay;
        DetailKeyText.Text = $"{entry.Namespace} / {entry.Key}";
        DetailReasonText.Text = string.IsNullOrWhiteSpace(entry.Translation)
            ? "Строка не переведена."
            : string.IsNullOrWhiteSpace(entry.ValidationSummary)
                ? "Проблема требует проверки."
                : entry.ValidationSummary;
        DetailTranslationText.Text = string.IsNullOrEmpty(entry.Translation)
            ? "— пусто —"
            : entry.Translation;
    }

    private void OpenInEditor_Click(object sender, RoutedEventArgs e)
        => OpenSelected();

    private void OpenSelected()
    {
        if (_viewModel is null || _selectedIssue is null)
            return;

        if (_viewModel.FilterAllCommand.CanExecute(null))
            _viewModel.FilterAllCommand.Execute(null);
        _viewModel.SearchText = string.Empty;
        _viewModel.SelectedEntry = _selectedIssue;
        _viewModel.EntriesView.MoveCurrentTo(_selectedIssue);
        OpenInEditorRequested?.Invoke(this, EventArgs.Empty);
    }

    private sealed record QaCategory(string Id, string Name, int Count);
}
