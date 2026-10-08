using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class QaPage : UserControl
{
    private MainViewModel? _viewModel;
    private readonly ObservableCollection<QaCategory> _categories = [];
    private QaIssueIndex? _qaIndex;
    private LocalizationEntry? _selectedIssue;
    private LocalizationEntry? _familyAnchor;
    private bool _familyOnly;
    private bool _suppressSelectionEvents;
    private bool _updatingDetails;
    private bool _cacheDirty;
    private bool _refreshPending = true;
    private bool _lastCommitRemovedCurrent;

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
        _qaIndex = null;
        _familyAnchor = null;
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
        var currentId = CurrentCategoryId();
        _qaIndex = new QaIssueIndex(document?.Entries);
        UpdateCategories(currentId);
        _cacheDirty = false;
        _refreshPending = false;
        RefreshIssues();
    }

    private void UpdateCategories(string? selectedId = null)
    {
        selectedId ??= CurrentCategoryId();
        _suppressSelectionEvents = true;
        try
        {
            _categories.Clear();
            AddCategory("all", "Все проблемы");
            AddCategory("untranslated", "Без перевода");
            AddCategory("sourceMissing", "Нет Original");
            AddCategory("tags", "Теги");
            AddCategory("placeholders", "Плейсхолдеры");
            AddCategory("newlines", "Переносы строк");
            AddCategory("glossary", "Глоссарий");
            AddCategory("consistency", "Разные переводы одного Original");
            AddCategory("sameSource", "Перевод совпадает с Original");
            AddCategory("suspicious", "Длина и QA-профили");

            CategoryList.SelectedItem = _categories.FirstOrDefault(x => x.Id == selectedId)
                                        ?? _categories.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void AddCategory(string id, string name)
        => _categories.Add(new QaCategory(id, name, _qaIndex?.Count(id) ?? 0));

    private string CurrentCategoryId()
        => (CategoryList.SelectedItem as QaCategory)?.Id ?? "all";

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        CommitQaTranslation();
        RefreshIssues();
    }

    private IReadOnlyList<LocalizationEntry> GetVisibleIssues(string categoryId)
    {
        if (_qaIndex is null)
            return [];

        if (!_familyOnly || _familyAnchor is null || _viewModel?.ActiveDocument is null)
            return _qaIndex.GetCategory(categoryId);

        var relations = EntryRelationIndex.For(_viewModel.ActiveDocument);
        return _qaIndex.GetFamilyCategory(categoryId, _familyAnchor, relations);
    }

    private bool IsVisibleInCurrentScope(LocalizationEntry entry, string categoryId)
    {
        if (_qaIndex is null || !_qaIndex.Contains(categoryId, entry))
            return false;

        if (!_familyOnly || _familyAnchor is null || _viewModel?.ActiveDocument is null)
            return true;

        var relations = EntryRelationIndex.For(_viewModel.ActiveDocument);
        var anchorFamily = relations.GetFamily(_familyAnchor);
        var entryFamily = relations.GetFamily(entry);
        return !string.IsNullOrEmpty(anchorFamily)
               && string.Equals(anchorFamily, entryFamily, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshIssues(int preferredIndex = 0, LocalizationEntry? preferredEntry = null)
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
        var visible = GetVisibleIssues(id);

        _suppressSelectionEvents = true;
        try
        {
            IssueGrid.ItemsSource = null;
            IssueGrid.ItemsSource = visible;
            IssueTitleText.Text = category?.Name ?? "Все проблемы";
            if (_familyOnly && _familyAnchor is not null)
            {
                var family = EntryRelationIndex.For(document).GetFamily(_familyAnchor);
                IssueCountText.Text = $"{visible.Count:N0} · группа {family}";
            }
            else
            {
                IssueCountText.Text = $"{visible.Count:N0}";
            }

            if (visible.Count == 0)
            {
                IssueGrid.SelectedItem = null;
            }
            else if (preferredEntry is not null && visible.Contains(preferredEntry))
            {
                IssueGrid.SelectedItem = preferredEntry;
            }
            else
            {
                var index = Math.Clamp(preferredIndex, 0, visible.Count - 1);
                IssueGrid.SelectedIndex = index;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        ShowDetails(IssueGrid.SelectedItem as LocalizationEntry);
        UpdateIssueNavigationState();
    }

    private void IssueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        CommitQaTranslation();
        ShowDetails(IssueGrid.SelectedItem as LocalizationEntry);
        UpdateIssueNavigationState();
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

            var categoryId = CurrentCategoryId();
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
        _lastCommitRemovedCurrent = false;
        if (_updatingDetails || _selectedIssue is null)
            return false;

        var edited = _selectedIssue;
        var next = DetailTranslationBox.Text ?? string.Empty;
        if (string.Equals(edited.Translation, next, StringComparison.Ordinal))
            return false;

        var previousIndex = Math.Max(0, IssueGrid.SelectedIndex);
        edited.Translation = next;

        var document = _viewModel?.ActiveDocument;
        if (document is null || _qaIndex is null)
        {
            _cacheDirty = true;
            return true;
        }

        var relations = EntryRelationIndex.For(document);
        _qaIndex.UpdateAfterTranslation(edited, relations);
        var categoryId = CurrentCategoryId();
        UpdateCategories(categoryId);

        var stillVisible = IsVisibleInCurrentScope(edited, categoryId);
        _lastCommitRemovedCurrent = !stillVisible;
        _cacheDirty = false;

        if (stillVisible)
        {
            RefreshIssues(previousIndex, edited);
        }
        else
        {
            RefreshIssues(previousIndex);
        }

        return true;
    }

    private void DetailTranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CommitQaTranslation();

    private void DetailTranslationBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        var moveNext = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        CommitQaTranslation();
        if (moveNext && !_lastCommitRemovedCurrent)
            MoveIssueSelection(1);
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
            var variants = _qaIndex?.GetConsistencyVariants(entry.Original) ?? [];
            return variants.Count == 0
                ? "Один и тот же Original имеет несколько вариантов перевода."
                : "Один и тот же Original имеет несколько вариантов перевода: " + string.Join(" | ", variants);
        }

        return string.IsNullOrWhiteSpace(entry.ValidationSummary)
            ? "Проблема требует проверки."
            : entry.ValidationSummary;
    }

    private void SetFamilyOnly(bool enabled)
    {
        _familyOnly = enabled;
        _familyAnchor = enabled ? _selectedIssue : null;
        RefreshIssues();
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
