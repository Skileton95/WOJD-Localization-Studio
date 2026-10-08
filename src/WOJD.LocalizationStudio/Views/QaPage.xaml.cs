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
    private readonly HashSet<string> _inconsistentOriginals = new(StringComparer.Ordinal);
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

        BuildConsistencyIndex(entries);

        _categories.Clear();
        _categories.Add(new QaCategory("all", "Все проблемы", entries.Count(IsAnyProblem)));
        _categories.Add(new QaCategory("untranslated", "Без перевода", entries.Count(x => string.IsNullOrWhiteSpace(x.Translation))));
        _categories.Add(new QaCategory("sourceMissing", "Нет Original", entries.Count(x => x.HasSourceMissingIssue || string.IsNullOrWhiteSpace(x.Original))));
        _categories.Add(new QaCategory("tags", "Теги", entries.Count(x => x.HasTagIssues)));
        _categories.Add(new QaCategory("placeholders", "Плейсхолдеры", entries.Count(x => x.HasPlaceholderIssues)));
        _categories.Add(new QaCategory("newlines", "Переносы строк", entries.Count(x => x.HasNewLineIssues)));
        _categories.Add(new QaCategory("glossary", "Глоссарий", entries.Count(x => x.HasGlossaryIssue)));
        _categories.Add(new QaCategory("consistency", "Разные переводы одного Original", entries.Count(IsConsistencyProblem)));
        _categories.Add(new QaCategory("sameSource", "Перевод совпадает с Original", entries.Count(x => x.HasSameAsSourceIssue)));
        _categories.Add(new QaCategory("suspicious", "Длина и QA-профили", entries.Count(x => x.HasSuspiciousLengthIssue || x.HasProfileRuleIssue)));

        CategoryList.SelectedItem = _categories.FirstOrDefault(x => x.Id == currentId) ?? _categories.FirstOrDefault();
        RefreshIssues();
    }

    private void BuildConsistencyIndex(IEnumerable<LocalizationEntry> entries)
    {
        _inconsistentOriginals.Clear();
        foreach (var group in entries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Original))
                     .GroupBy(x => x.Original, StringComparer.Ordinal))
        {
            var variants = group
                .Select(x => x.Translation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count();
            if (variants > 1)
                _inconsistentOriginals.Add(group.Key);
        }
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

    private bool Matches(string id, LocalizationEntry entry)
        => id switch
        {
            "untranslated" => string.IsNullOrWhiteSpace(entry.Translation),
            "sourceMissing" => entry.HasSourceMissingIssue || string.IsNullOrWhiteSpace(entry.Original),
            "tags" => entry.HasTagIssues,
            "placeholders" => entry.HasPlaceholderIssues,
            "newlines" => entry.HasNewLineIssues,
            "glossary" => entry.HasGlossaryIssue,
            "consistency" => IsConsistencyProblem(entry),
            "sameSource" => entry.HasSameAsSourceIssue,
            "suspicious" => entry.HasSuspiciousLengthIssue || entry.HasProfileRuleIssue,
            _ => IsAnyProblem(entry)
        };

    private bool IsAnyProblem(LocalizationEntry entry)
        => string.IsNullOrWhiteSpace(entry.Translation)
           || entry.HasValidationIssues
           || IsConsistencyProblem(entry);

    private bool IsConsistencyProblem(LocalizationEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Original)
           && _inconsistentOriginals.Contains(entry.Original);

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

        var categoryId = (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";
        DetailStateText.Text = GetStateText(entry, categoryId);
        DetailOriginalText.Text = entry.OriginalDisplay;
        DetailKeyText.Text = $"{entry.Namespace} / {entry.Key}";
        DetailReasonText.Text = GetReason(entry, categoryId);
        DetailTranslationText.Text = string.IsNullOrEmpty(entry.Translation)
            ? "— пусто —"
            : entry.Translation;
    }

    private string GetStateText(LocalizationEntry entry, string categoryId)
        => categoryId switch
        {
            "untranslated" => "Отсутствует перевод",
            "sourceMissing" => "Original отсутствует",
            "tags" => "Ошибка тегов",
            "placeholders" => "Ошибка плейсхолдеров",
            "newlines" => "Ошибка переносов",
            "glossary" => "Терминология",
            "consistency" => "Несогласованный перевод",
            "sameSource" => "Перевод совпадает с Original",
            "suspicious" => "Требуется проверка",
            _ => string.IsNullOrWhiteSpace(entry.Translation) ? "Отсутствует перевод" : entry.QaStateText
        };

    private string GetReason(LocalizationEntry entry, string categoryId)
    {
        if (categoryId == "untranslated")
            return "Строка не переведена.";
        if (categoryId == "sourceMissing")
            return "Исходный текст отсутствует. Структуру тегов и плейсхолдеров сравнить невозможно.";
        if (categoryId == "consistency")
        {
            var variants = _viewModel?.ActiveDocument?.Entries
                .Where(x => string.Equals(x.Original, entry.Original, StringComparison.Ordinal))
                .Select(x => x.Translation)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToList() ?? [];
            return "Один и тот же Original имеет несколько вариантов перевода: " + string.Join(" | ", variants);
        }

        return string.IsNullOrWhiteSpace(entry.ValidationSummary)
            ? "Проблема требует проверки."
            : entry.ValidationSummary;
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
