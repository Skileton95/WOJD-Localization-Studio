using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class GlossaryWindow : Window
{
    private readonly GlossaryService _service;
    private readonly IReadOnlyList<LocalizationDocument> _documents;
    private readonly LocalizationDocument? _activeDocument;
    private readonly Action<LocalizationDocument, LocalizationEntry>? _navigate;
    private readonly ICollectionView _view;
    private GlossaryEntry? _selected;

    public GlossaryWindow(
        GlossaryService service,
        IReadOnlyList<LocalizationDocument> documents,
        LocalizationDocument? activeDocument,
        Action<LocalizationDocument, LocalizationEntry>? navigate)
    {
        InitializeComponent();

        _service = service;
        _documents = documents;
        _activeDocument = activeDocument;
        _navigate = navigate;

        GlossaryGrid.ItemsSource = _service.Entries;

        _view = CollectionViewSource.GetDefaultView(_service.Entries);
        _view.Filter = FilterEntry;

        RefreshSummary();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not GlossaryEntry entry)
            return false;

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(entry.Source, query)
               || Contains(entry.Translation, query)
               || Contains(entry.AllowedText, query)
               || Contains(entry.ScopeText, query)
               || Contains(entry.Note, query);
    }

    private static bool Contains(string value, string query) =>
        (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _view.Refresh();

    private void GlossaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = GlossaryGrid.SelectedItem as GlossaryEntry;

        if (_selected is null)
        {
            ClearEditor();
            return;
        }

        EditorTitle.Text = "Редактирование термина";
        SourceBox.Text = _selected.Source;
        TranslationBox.Text = _selected.Translation;
        AllowedTranslationsBox.Text = string.Join(
            Environment.NewLine,
            _selected.AllowedTranslations);
        NamespaceScopesBox.Text = string.Join(
            Environment.NewLine,
            _selected.NamespaceScopes);
        PriorityBox.Text = _selected.Priority.ToString();
        NoteBox.Text = _selected.Note;
        LockedCheckBox.IsChecked = _selected.IsLocked;

        DeleteButton.IsEnabled = true;
        WhereUsedButton.IsEnabled = true;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        GlossaryGrid.SelectedItem = null;
        ClearEditor();
        SourceBox.Focus();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim();
        var translation = TranslationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(translation))
        {
            MessageBox.Show(
                this,
                "Заполните китайский термин и основной русский перевод.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PriorityBox.Text.Trim(), out var priority))
        {
            MessageBox.Show(
                this,
                "Приоритет должен быть целым числом.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PriorityBox.Focus();
            return;
        }

        var allowed = ParseList(AllowedTranslationsBox.Text);
        var scopes = ParseList(NamespaceScopesBox.Text);

        var duplicate = _service.Entries.FirstOrDefault(entry =>
            !ReferenceEquals(entry, _selected) &&
            string.Equals(entry.Source, source, StringComparison.Ordinal) &&
            ScopesEqual(entry.NamespaceScopes, scopes));

        if (duplicate is not null)
        {
            MessageBox.Show(
                this,
                "Такой китайский термин уже существует с той же областью Namespace.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_selected is null)
        {
            _selected = new GlossaryEntry();
            _service.Entries.Add(_selected);
        }

        _selected.Source = source;
        _selected.Translation = translation;
        _selected.AllowedTranslations = allowed;
        _selected.NamespaceScopes = scopes;
        _selected.Priority = priority;
        _selected.Note = NoteBox.Text.Trim();
        _selected.IsLocked = LockedCheckBox.IsChecked == true;

        await _service.SaveAsync();

        _view.Refresh();
        GlossaryGrid.SelectedItem = _selected;
        GlossaryGrid.ScrollIntoView(_selected);

        RefreshSummary();

        var conflicts = _service.GetConflicts();
        StatusText.Text = conflicts.Count == 0
            ? $"Сохранено: {source}"
            : $"Сохранено: {source} • конфликтов глоссария: {conflicts.Count:N0}";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        var result = MessageBox.Show(
            this,
            $"Удалить термин «{_selected.Source}»?",
            "Глоссарий",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var source = _selected.Source;
        _service.Entries.Remove(_selected);
        _selected = null;

        await _service.SaveAsync();

        ClearEditor();
        RefreshSummary();
        StatusText.Text = $"Удалено: {source}";
    }

    private void WhereUsedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        var rows = _documents
            .SelectMany(document => document.Entries
                .Where(entry =>
                    _selected.AppliesToNamespace(entry.Namespace) &&
                    entry.Source.Contains(_selected.Source, StringComparison.Ordinal))
                .Select(entry => new GlossaryUsageRow(
                    document,
                    entry,
                    string.IsNullOrWhiteSpace(entry.Translation) ||
                    _selected.IsTranslationAccepted(entry.Translation))))
            .ToList();

        var window = new GlossaryUsageWindow(
            _selected,
            rows)
        {
            Owner = this
        };

        if (window.ShowDialog() == true &&
            window.SelectedUsage is not null)
        {
            _navigate?.Invoke(
                window.SelectedUsage.Document,
                window.SelectedUsage.Entry);

            Close();
        }
    }

    private void ConflictsButton_Click(object sender, RoutedEventArgs e)
    {
        var conflicts = _service.GetConflicts();

        var window = new GlossaryConflictWindow(conflicts)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void CandidatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDocument is null)
        {
            MessageBox.Show(
                this,
                "Сначала откройте файл локализации и сделайте его активным.",
                "Кандидаты в глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var candidates = GlossaryAnalysisService.FindCandidates(
            _activeDocument,
            _service.Entries);

        var window = new GlossaryCandidateWindow(candidates)
        {
            Owner = this
        };

        if (window.ShowDialog() != true ||
            window.SelectedCandidate is null)
            return;

        GlossaryGrid.SelectedItem = null;
        ClearEditor();

        SourceBox.Text = window.SelectedCandidate.Source;
        TranslationBox.Text = window.SelectedCandidate.SuggestedTranslation;
        EditorTitle.Text = "Новый термин из кандидата";
        SourceBox.Focus();

        StatusText.Text =
            $"Кандидат: {window.SelectedCandidate.Source} • " +
            $"вхождений: {window.SelectedCandidate.Occurrences:N0}";
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Импорт глоссария"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var mode = MessageBox.Show(
            this,
            "Объединить импортируемые термины с текущим глоссарием?\n\n" +
            "Да — объединить и обновить совпадающие термины.\n" +
            "Нет — полностью заменить текущий глоссарий.\n" +
            "Отмена — ничего не импортировать.",
            "Импорт глоссария",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (mode == MessageBoxResult.Cancel)
            return;

        try
        {
            var changed = await _service.ImportAsync(
                dialog.FileName,
                replace: mode == MessageBoxResult.No);

            _view.Refresh();
            RefreshSummary();
            ClearEditor();

            StatusText.Text =
                $"Импортировано/обновлено терминов: {changed:N0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось импортировать глоссарий.\n\n{ex.Message}",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "wojd-glossary.json",
            Title = "Экспорт глоссария"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await _service.ExportAsync(dialog.FileName);
            StatusText.Text =
                $"Экспортировано терминов: {_service.Entries.Count:N0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось экспортировать глоссарий.\n\n{ex.Message}",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearEditor()
    {
        EditorTitle.Text = "Новый термин";
        SourceBox.Clear();
        TranslationBox.Clear();
        AllowedTranslationsBox.Clear();
        NamespaceScopesBox.Clear();
        PriorityBox.Text = "0";
        NoteBox.Clear();
        LockedCheckBox.IsChecked = true;
        DeleteButton.IsEnabled = false;
        WhereUsedButton.IsEnabled = false;
    }

    private void RefreshSummary()
    {
        var locked = _service.Entries.Count(entry => entry.IsLocked);
        var scoped = _service.Entries.Count(entry => entry.NamespaceScopes.Count > 0);
        var conflicts = _service.GetConflicts().Count;

        GlossarySummaryText.Text =
            $"Терминов: {_service.Entries.Count:N0} • закреплено: {locked:N0} • " +
            $"ограничено Namespace: {scoped:N0} • конфликтов: {conflicts:N0}";
    }

    private static List<string> ParseList(string value) =>
        (value ?? string.Empty)
            .Split(
                ['\r', '\n', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool ScopesEqual(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }
}
