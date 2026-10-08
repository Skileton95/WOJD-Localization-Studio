using System.Collections.ObjectModel;
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
    private readonly Dictionary<string, List<LocalizationEntry>> _issueCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inconsistentOriginals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _consistencyVariants = new(StringComparer.Ordinal);
    private LocalizationEntry? _selectedIssue;
    private bool _suppressSelectionEvents;
    private bool _updatingDetails;
    private bool _cacheDirty;
    private bool _refreshPending = true;

    public event EventHandler? OpenInEditorRequested;

    public QaPage()
    {
        InitializeComponent();
        CategoryList.ItemsSource = _categories;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && (_refreshPending || _cacheDirty))
                Refresh();
        };
    }

    public void Attach(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _refreshPending = true;
        if (IsVisible)
            Refresh();
    }

    public void Detach()
    {
        CommitQaTranslation();
        _viewModel = null;
        _refreshPending = true;
    }

    public void Refresh()
    {
        CommitQaTranslation();

        if (!IsVisible)
        {
            _refreshPending = true;
            return;
        }

        var document = _viewModel?.ActiveDocument;
        var entries = document?.Entries;
        var currentId = (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";

        BuildIssueCache(entries);

        _suppressSelectionEvents = true;
        try
        {
            _categories.Clear();
            _categories.Add(new QaCategory("all", "Все проблемы", CountCached("all")));
            _categories.Add(new QaCategory("untranslated", "Без перевода", CountCached("untranslated")));
            _categories.Add(new QaCategory("sourceMissing", "Нет Original", CountCached("sourceMissing")));
            _categories.Add(new QaCategory("tags", "Теги", CountCached("tags")));
            _categories.Add(new QaCategory("placeholders", "Плейсхолдеры", CountCached("placeholders")));
            _categories.Add(new QaCategory("newlines", "Переносы строк", CountCached("newlines")));
            _categories.Add(new QaCategory("glossary", "Глоссарий", CountCached("glossary")));
            _categories.Add(new QaCategory("consistency", "Разные переводы одного Original", CountCached("consistency")));
            _categories.Add(new QaCategory("sameSource", "Перевод совпадает с Original", CountCached("sameSource")));
            _categories.Add(new QaCategory("suspicious", "Длина и QA-профили", CountCached("suspicious")));

            CategoryList.SelectedItem = _categories.FirstOrDefault(x => x.Id == currentId)
                                        ?? _categories.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        _cacheDirty = false;
        _refreshPending = false;
        RefreshIssues();
    }

    private void BuildIssueCache(IReadOnlyList<LocalizationEntry>? entries)
    {
        _issueCache.Clear();
        _inconsistentOriginals.Clear();
        _consistencyVariants.Clear();

        foreach (var id in new[]
                 {
                     "all", "untranslated", "sourceMissing", "tags", "placeholders",
                     "newlines", "glossary", "consistency", "sameSource", "suspicious"
                 })
        {
            _issueCache[id] = [];
        }

        if (entries is null || entries.Count == 0)
            return;

        var firstTranslationByOriginal = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Original) || string.IsNullOrWhiteSpace(entry.Translation))
                continue;

            if (!firstTranslationByOriginal.TryGetValue(entry.Original, out var first))
            {
                firstTranslationByOriginal[entry.Original] = entry.Translation;
                continue;
            }

            if (!string.Equals(first, entry.Translation, StringComparison.Ordinal))
                _inconsistentOriginals.Add(entry.Original);
        }

        foreach (var entry in entries)
        {
            var consistency = IsConsistencyProblem(entry);
            var any = string.IsNullOrWhiteSpace(entry.Translation)
                      || entry.HasValidationIssues
                      || consistency;

            if (any)
                _issueCache["all"].Add(entry);
            if (string.IsNullOrWhiteSpace(entry.Translation))
                _issueCache["untranslated"].Add(entry);
            if (entry.HasSourceMissingIssue || string.IsNullOrWhiteSpace(entry.Original))
                _issueCache["sourceMissing"].Add(entry);
            if (entry.HasTagIssues)
                _issueCache["tags"].Add(entry);
            if (entry.HasPlaceholderIssues)
                _issueCache["placeholders"].Add(entry);
            if (entry.HasNewLineIssues)
                _issueCache["newlines"].Add(entry);
            if (entry.HasGlossaryIssue)
                _issueCache["glossary"].Add(entry);
            if (consistency)
            {
                _issueCache["consistency"].Add(entry);
                CacheConsistencyVariant(entry);
            }
            if (entry.HasSameAsSourceIssue)
                _issueCache["sameSource"].Add(entry);
            if (entry.HasSuspiciousLengthIssue || entry.HasProfileRuleIssue)
                _issueCache["suspicious"].Add(entry);
        }
    }

    private void CacheConsistencyVariant(LocalizationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Original) || string.IsNullOrWhiteSpace(entry.Translation))
            return;

        if (!_consistencyVariants.TryGetValue(entry.Original, out var variants))
        {
            variants = [];
            _consistencyVariants[entry.Original] = variants;
        }

        if (variants.Count >= 6 || variants.Contains(entry.Translation, StringComparer.Ordinal))
            return;

        variants.Add(entry.Translation);
    }

    private int CountCached(string id)
        => _issueCache.TryGetValue(id, out var items) ? items.Count : 0;

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        CommitQaTranslation();
        if (_cacheDirty)
        {
            Refresh();
            return;
        }

        RefreshIssues();
    }

    private void RefreshIssues()
    {
        var document = _viewModel?.ActiveDocument;
        if (document is null)
        {
            IssueGrid.ItemsSource = null;
            IssueTitleText.Text = "Проверка";
            IssueCountText.Text = "Файл не открыт";
            ShowDetails(null);
            return;
        }

        var category = CategoryList.SelectedItem as QaCategory;
        var id = category?.Id ?? "all";
        var filtered = _issueCache.TryGetValue(id, out var cached)
            ? cached
            : [];

        _suppressSelectionEvents = true;
        try
        {
            IssueGrid.ItemsSource = filtered;
            IssueTitleText.Text = category?.Name ?? "Все проблемы";
            IssueCountText.Text = $"{filtered.Count:N0}";
            IssueGrid.SelectedItem = filtered.Count > 0 ? filtered[0] : null;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

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
    {
        if (_suppressSelectionEvents)
            return;

        CommitQaTranslation();
        ShowDetails(IssueGrid.SelectedItem as LocalizationEntry);
    }

    private void IssueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueGrid.SelectedItem is LocalizationEntry)
            OpenSelected();
    }

    private void ShowDetails(LocalizationEntry? entry)
    {
        _selectedIssue = entry;
        OpenInEditorButton.IsEnabled = entry is not null;
        ApplyTranslationButton.IsEnabled = entry is not null;
        DetailTranslationBox.IsEnabled = entry is not null;

        _updatingDetails = true;
        try
        {
            if (entry is null)
            {
                DetailStateText.Text = "Выберите проблему";
                DetailOriginalText.Text = string.Empty;
                DetailKeyText.Text = string.Empty;
                DetailReasonText.Text = string.Empty;
                DetailTranslationBox.Text = string.Empty;
                return;
            }

            var categoryId = (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";
            DetailStateText.Text = GetStateText(entry, categoryId);
            DetailOriginalText.Text = entry.OriginalDisplay;
            DetailKeyText.Text = $"{entry.Namespace} / {entry.Key}";
            DetailReasonText.Text = GetReason(entry, categoryId);
            DetailTranslationBox.Text = entry.Translation ?? string.Empty;
        }
        finally
        {
            _updatingDetails = false;
        }
    }

    private bool CommitQaTranslation()
    {
        if (_updatingDetails || _selectedIssue is null)
            return false;

        var next = DetailTranslationBox.Text ?? string.Empty;
        if (string.Equals(_selectedIssue.Translation, next, StringComparison.Ordinal))
            return false;

        _selectedIssue.Translation = next;
        _cacheDirty = true;

        var categoryId = (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";
        DetailStateText.Text = GetStateText(_selectedIssue, categoryId);
        DetailReasonText.Text = GetReason(_selectedIssue, categoryId);
        IssueGrid.Items.Refresh();
        return true;
    }

    private void DetailTranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CommitQaTranslation();

    private void DetailTranslationBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        CommitQaTranslation();
        e.Handled = true;
    }

    private void ApplyTranslation_Click(object sender, RoutedEventArgs e)
        => CommitQaTranslation();

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
            var variants = _consistencyVariants.TryGetValue(entry.Original, out var cached)
                ? cached
                : [];
            return variants.Count == 0
                ? "Один и тот же Original имеет несколько вариантов перевода."
                : "Один и тот же Original имеет несколько вариантов перевода: " + string.Join(" | ", variants);
        }

        return string.IsNullOrWhiteSpace(entry.ValidationSummary)
            ? "Проблема требует проверки."
            : entry.ValidationSummary;
    }

    private void OpenInEditor_Click(object sender, RoutedEventArgs e)
        => OpenSelected();

    private void OpenSelected()
    {
        CommitQaTranslation();

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
