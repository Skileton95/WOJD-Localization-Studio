using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class MainWindow : Window
{
    private readonly NdjsonService _service = new();
    private readonly UpdateService _updateService = new();
    private readonly UpdateSessionService _updateSessionService = new();
    private readonly OpenAiCorrectionService _openAiCorrectionService = new();
    private readonly TranslationHistoryService _translationHistoryService = new();
    private readonly TranslationMemoryService _translationMemoryService = new();
    private readonly GlossaryService _glossaryService = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ObservableCollection<LocalizationDocument> _documents = new();
    private ICollectionView? _view;
    private LocalizationDocument? _currentDocument;
    private LocalizationEntry? _selected;
    private bool _suppressEditor;
    private bool _isCheckingForUpdates;
    private bool _isUpdating;
    private bool _isAiFixing;
    private bool _isMassFixing;
    private bool _allowCloseWithoutPrompt;
    private UpdateInfo? _availableUpdate;
    private string _statusFilter = "All";
    private string? _namespaceFilter;
    private ValidationIssueKind _validationTypeFilter = ValidationIssueKind.None;
    private readonly Dictionary<string, List<EntryLocation>> _sourceIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _conflictingSources = new(StringComparer.Ordinal);
    private readonly HashSet<LocalizationEntry> _glossaryMismatchEntries = new();
    private readonly Stack<EditBatch> _undoStack = new();
    private readonly Stack<EditBatch> _redoStack = new();

    public MainWindow()
    {
        InitializeComponent();
        FilesList.ItemsSource = _documents;
        UpdateFilterVisuals();

        Loaded += MainWindow_Loaded;
        _updateTimer.Tick += UpdateTimer_Tick;

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, async (_, _) => await OpenFilesAsync()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, async (_, _) => await SaveCurrentAsync()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = $"v{_updateService.CurrentVersion} • проверка…";

        try
        {
            await Task.WhenAll(
                _glossaryService.LoadAsync(),
                _translationMemoryService.LoadAsync());
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось загрузить локальные данные: {ex.Message}";
        }

        await RestoreUpdateSessionAsync();

        _updateTimer.Start();
        await Task.Delay(900);
        await CheckForUpdatesAsync();
    }

    private async void UpdateTimer_Tick(object? sender, EventArgs e) => await CheckForUpdatesAsync();

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        if (_availableUpdate is null)
            await CheckForUpdatesAsync(manual: true);
        else
            await BeginInAppUpdateAsync();
    }

    private async Task CheckForUpdatesAsync(bool manual = false)
    {
        if (_isCheckingForUpdates || _isUpdating) return;

        _isCheckingForUpdates = true;

        if (manual)
        {
            UpdateStatusText.Text = "Проверка обновлений…";
            UpdateButton.IsEnabled = false;
            UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0x7E, 0x94, 0xA7));
        }

        try
        {
            var update = await _updateService.CheckAsync();
            _availableUpdate = update;

            if (update is null)
            {
                UpdateStatusText.Text = $"v{_updateService.CurrentVersion} • актуальная";
                UpdateStatusText.ToolTip = null;
                UpdateButton.Content = "Проверить";
                UpdateButton.IsEnabled = true;
                UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0x37, 0x9A, 0x58));
                if (manual) StatusText.Text = "Установлена актуальная версия";
                return;
            }

            UpdateStatusText.Text = $"Доступна v{update.Version}";
            UpdateStatusText.ToolTip = string.IsNullOrWhiteSpace(update.Notes) ? null : update.Notes;
            UpdateButton.Content = "Обновить";
            UpdateButton.IsEnabled = true;
            UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x99, 0x21));
        }
        catch (Exception ex)
        {
            if (manual)
            {
                UpdateStatusText.Text = "Ошибка проверки";
                UpdateStatusText.ToolTip = ex.Message;
                UpdateButton.Content = "Повторить";
                UpdateButton.IsEnabled = true;
                UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
                StatusText.Text = "Не удалось проверить обновления";
            }
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private async Task BeginInAppUpdateAsync()
    {
        if (_availableUpdate is null || _isUpdating) return;

        if (_documents.Any(d => d.IsDirty))
        {
            var saveResult = MessageBox.Show(
                this,
                "Есть несохранённые изменения. Сохранить их перед обновлением?",
                "Обновление",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (saveResult == MessageBoxResult.Cancel)
                return;

            if (saveResult == MessageBoxResult.Yes)
            {
                await SaveAllAsync();
                if (_documents.Any(d => d.IsDirty))
                    return;
            }
        }

        await _updateSessionService.SaveAsync(
            _documents.Select(d => d.FilePath),
            _currentDocument?.FilePath);

        _isUpdating = true;
        _updateTimer.Stop();
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Обновление";
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdatePercentText.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdatePercentText.Text = "0%";
        UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xEA));

        var progress = new Progress<UpdateProgress>(value =>
        {
            UpdateProgressBar.Value = value.Percent;
            UpdatePercentText.Text = $"{value.Percent}%";
            UpdateStatusText.Text = value.Message;
            StatusText.Text = $"Обновление: {value.Message} — {value.Percent}%";
        });

        try
        {
            await _updateService.PrepareUpdateAsync(progress);
            await Task.Delay(350);

            _allowCloseWithoutPrompt = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _updateSessionService.Clear();
            _isUpdating = false;
            _updateTimer.Start();

            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdatePercentText.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "Ошибка обновления";
            UpdateStatusText.ToolTip = ex.Message;
            UpdateButton.Content = "Повторить";
            UpdateButton.IsEnabled = true;
            UpdateDot.Fill = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
            StatusText.Text = "Обновление не установлено. Наведите курсор на статус обновления для подробностей.";
        }
    }

    private async Task RestoreUpdateSessionAsync()
    {
        UpdateSession? session;

        try
        {
            session = await _updateSessionService.LoadAndConsumeAsync();
        }
        catch
        {
            return;
        }

        if (session is null || session.FilePaths.Length == 0)
            return;

        var existingPaths = session.FilePaths
            .Where(System.IO.File.Exists)
            .ToArray();

        if (existingPaths.Length == 0)
            return;

        StatusText.Text = "Восстановление открытых файлов после обновления…";
        await OpenFilesFromPathsAsync(existingPaths, session.ActiveFilePath);

        StatusText.Text = $"Сессия восстановлена: {_documents.Count:N0} файлов";
    }

    private async Task OpenFilesFromPathsAsync(IEnumerable<string> paths, string? preferredActivePath = null)
    {
        LocalizationDocument? lastOpened = null;

        foreach (var path in paths)
        {
            var existing = _documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                lastOpened = existing;
                continue;
            }

            try
            {
                StatusText.Text = $"Загрузка: {System.IO.Path.GetFileName(path)}…";

                var loaded = await Task.Run(async () =>
                {
                    var entries = await _service.LoadAsync(path).ConfigureAwait(false);
                    ValidationService.ValidateAll(entries);
                    return entries;
                });

                var document = new LocalizationDocument(path, loaded);
                document.RefreshComputedProperties();
                _documents.Add(document);
                lastOpened = document;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Не удалось открыть {System.IO.Path.GetFileName(path)}.\n\n{ex.Message}",
                    "Ошибка открытия",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredActivePath))
        {
            var preferred = _documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, preferredActivePath, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
                lastOpened = preferred;
        }

        RebuildSourceIndex();

        if (lastOpened is not null)
            FilesList.SelectedItem = lastOpened;

        UpdateButtons();
        UpdateCounters();
    }

    private void RebuildSourceIndex()
    {
        _sourceIndex.Clear();
        _conflictingSources.Clear();

        foreach (var document in _documents)
        {
            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrEmpty(entry.Source))
                    continue;

                if (!_sourceIndex.TryGetValue(entry.Source, out var list))
                {
                    list = new List<EntryLocation>();
                    _sourceIndex.Add(entry.Source, list);
                }

                list.Add(new EntryLocation(document, entry));
            }
        }

        foreach (var source in _sourceIndex.Keys)
            RefreshConflictState(source);
    }

    private IReadOnlyList<EntryLocation> GetExactSourceMatches(LocalizationEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Source))
            return Array.Empty<EntryLocation>();

        return _sourceIndex.TryGetValue(entry.Source, out var matches)
            ? matches
            : Array.Empty<EntryLocation>();
    }

    private void RefreshConflictState(string source)
    {
        if (string.IsNullOrEmpty(source) || !_sourceIndex.TryGetValue(source, out var matches))
        {
            _conflictingSources.Remove(source);
            return;
        }

        string? firstTranslation = null;
        var hasConflict = false;

        foreach (var item in matches)
        {
            var translation = item.Entry.Translation;
            if (string.IsNullOrWhiteSpace(translation))
                continue;

            if (firstTranslation is null)
            {
                firstTranslation = translation;
                continue;
            }

            if (!string.Equals(firstTranslation, translation, StringComparison.Ordinal))
            {
                hasConflict = true;
                break;
            }
        }

        if (hasConflict)
            _conflictingSources.Add(source);
        else
            _conflictingSources.Remove(source);
    }

    private bool HasTranslationConflict(LocalizationEntry entry) =>
        !string.IsNullOrEmpty(entry.Source) && _conflictingSources.Contains(entry.Source);

    private async void CloseFileButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isUpdating ||
            sender is not Button button ||
            button.DataContext is not LocalizationDocument document)
            return;

        if (document.IsDirty)
        {
            var result = MessageBox.Show(
                this,
                $"В файле «{document.FileName}» есть несохранённые изменения.\n\nСохранить их перед закрытием?",
                "Закрыть файл",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes &&
                !await SaveDocumentAsync(document))
                return;
        }

        var index = _documents.IndexOf(document);
        var wasCurrent = ReferenceEquals(document, _currentDocument);

        _documents.Remove(document);

        // История может содержать массовые изменения сразу в нескольких файлах.
        // После закрытия файла сбрасываем её, чтобы Undo/Redo не меняли уже закрытый документ.
        _undoStack.Clear();
        _redoStack.Clear();

        RebuildSourceIndex();

        if (_documents.Count == 0)
        {
            _currentDocument = null;
            _selected = null;
            _namespaceFilter = null;
            EntriesGrid.ItemsSource = null;
            _view = null;
            ClearEditor();
            UpdateCounters();
            UpdateButtons();
            StatusText.Text = $"Закрыт файл: {document.FileName}";
            return;
        }

        if (wasCurrent)
        {
            FilesList.SelectedIndex = Math.Min(Math.Max(index, 0), _documents.Count - 1);
        }
        else
        {
            _view?.Refresh();
            UpdateExactMatchesPanel();
            UpdateCounters();
            UpdateButtons();
        }

        StatusText.Text = $"Закрыт файл: {document.FileName}";
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e) => await OpenFilesAsync();

    private async Task OpenFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "WOJD NDJSON (*.ndjson)|*.ndjson|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
            Title = "Открыть файлы русификатора"
        };

        if (dialog.ShowDialog(this) != true) return;

        await OpenFilesFromPathsAsync(dialog.FileNames);

        StatusText.Text = _documents.Count == 0
            ? "Файлы не открыты"
            : $"Открыто файлов: {_documents.Count}";
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _namespaceFilter = null;
        _currentDocument = FilesList.SelectedItem as LocalizationDocument;
        _selected = null;

        if (_currentDocument is null)
        {
            EntriesGrid.ItemsSource = null;
            _view = null;
            ClearEditor();
            UpdateCounters();
            UpdateButtons();
            return;
        }

        EntriesGrid.ItemsSource = _currentDocument.Entries;
        RebuildGlossaryMismatchCache();
        SetupView();
        UpdateCounters();
        UpdateButtons();
        StatusText.Text = $"Активный файл: {_currentDocument.FileName}";

        if (_currentDocument.Entries.Count > 0)
            EntriesGrid.SelectedIndex = 0;
    }

    private void SetupView()
    {
        if (_currentDocument is null)
        {
            _view = null;
            return;
        }

        _view = CollectionViewSource.GetDefaultView(_currentDocument.Entries);
        ApplyViewFilter();
    }

    private void ApplyViewFilter()
    {
        if (_view is null)
            return;

        var hasFilter =
            _namespaceFilter is not null ||
            _validationTypeFilter != ValidationIssueKind.None ||
            !string.Equals(_statusFilter, "All", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(SearchBox.Text);

        Predicate<object>? desiredFilter = hasFilter ? FilterEntry : null;

        if (!Equals(_view.Filter, desiredFilter))
            _view.Filter = desiredFilter;
        else if (hasFilter)
            _view.Refresh();

        UpdateRowNavigationButtons();
    }

    private void RefreshFilteredViewPreservingSelection()
    {
        if (_view is null)
            return;

        // При обычном режиме «Все» обновлять CollectionView после каждого символа
        // не нужно: это сбрасывало выделение строки на больших файлах.
        if (_view.Filter is null)
        {
            UpdateRowNavigationButtons();
            return;
        }

        var selected = _selected;
        _view.Refresh();

        if (selected is not null && _view.Contains(selected))
            EntriesGrid.SelectedItem = selected;

        UpdateRowNavigationButtons();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not LocalizationEntry entry) return false;

        if (_namespaceFilter is not null &&
            !string.Equals(entry.Namespace, _namespaceFilter, StringComparison.Ordinal))
            return false;

        var matchesStatus = _statusFilter switch
        {
            "Translated" => !string.IsNullOrWhiteSpace(entry.Translation),
            "Untranslated" => string.IsNullOrWhiteSpace(entry.Translation),
            "Errors" => entry.HasValidationIssues,
            "Conflicts" => HasTranslationConflict(entry),
            "Glossary" => HasGlossaryMismatch(entry),
            "Modified" => entry.IsModified,
            _ => true
        };

        if (!matchesStatus) return false;

        if (_validationTypeFilter != ValidationIssueKind.None &&
            !ValidationService.HasIssueKind(entry, _validationTypeFilter))
            return false;

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        var scope = (SearchScopeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все поля";

        return scope switch
        {
            "Key" => Contains(entry.Key, query),
            "Namespace" => Contains(entry.Namespace, query),
            "Китайский" => Contains(entry.Source, query),
            "Русский" => Contains(entry.Translation, query),
            _ => Contains(entry.Key, query)
                 || Contains(entry.Namespace, query)
                 || Contains(entry.Source, query)
                 || Contains(entry.Translation, query)
        };
    }

    private static bool Contains(string value, string query) =>
        (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyViewFilter();
    private void SearchScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyViewFilter();

    private void ValidationTypeFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        var tag = (ValidationTypeFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

        _validationTypeFilter = Enum.TryParse<ValidationIssueKind>(tag, out var kind)
            ? kind
            : ValidationIssueKind.None;

        if (_validationTypeFilter != ValidationIssueKind.None)
            _statusFilter = "Errors";

        UpdateFilterVisuals();
        ApplyViewFilter();
    }

    private void FilterBadge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filter)
            return;

        _statusFilter = filter;

        if (string.Equals(filter, "All", StringComparison.Ordinal))
        {
            _namespaceFilter = null;
            _validationTypeFilter = ValidationIssueKind.None;
            if (ValidationTypeFilterBox.SelectedIndex != 0)
                ValidationTypeFilterBox.SelectedIndex = 0;
        }

        UpdateFilterVisuals();
        ApplyViewFilter();

        if (EntriesGrid.SelectedItem is LocalizationEntry selected && _view is not null && !_view.Contains(selected))
            EntriesGrid.SelectedItem = null;
    }

    private void UpdateFilterVisuals()
    {
        var buttons = new[]
        {
            AllFilterButton,
            TranslatedFilterButton,
            UntranslatedFilterButton,
            ErrorFilterButton,
            ConflictFilterButton,
            GlossaryFilterButton,
            ModifiedFilterButton
        };

        foreach (var button in buttons)
        {
            var selected = string.Equals(button.Tag?.ToString(), _statusFilter, StringComparison.Ordinal);
            button.BorderBrush = new SolidColorBrush(selected
                ? Color.FromRgb(0x2F, 0x7B, 0xEA)
                : Color.FromRgb(0xD6, 0xE1, 0xEC));
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void NamespaceCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not LocalizationEntry entry)
            return;

        _namespaceFilter = entry.Namespace;
        _statusFilter = "All";
        _validationTypeFilter = ValidationIssueKind.None;
        if (ValidationTypeFilterBox.SelectedIndex != 0)
            ValidationTypeFilterBox.SelectedIndex = 0;
        SearchBox.Clear();

        UpdateFilterVisuals();
        ApplyViewFilter();

        StatusText.Text = $"Показаны все строки Namespace: {entry.Namespace}";
        e.Handled = true;
    }

    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = EntriesGrid.SelectedItem as LocalizationEntry;
        _suppressEditor = true;

        if (_selected is null)
        {
            ClearEditor();
            _suppressEditor = false;
            return;
        }

        SelectedKey.Text = _selected.Key;
        SelectedNamespace.Text = string.IsNullOrWhiteSpace(_selected.Namespace)
            ? "Namespace: —"
            : $"Namespace: {_selected.Namespace}";
        SourceText.Text = string.IsNullOrWhiteSpace(_selected.Source) ? "—" : _selected.Source;
        TranslationBox.IsEnabled = true;
        TranslationBox.Text = _selected.Translation;
        LengthLabel.Text = $"{_selected.Translation.Length:N0} символов";
        TranslationVariantsButton.IsEnabled = !_isAiFixing && !string.IsNullOrWhiteSpace(_selected.Source);
        TranslationHistoryButton.IsEnabled = true;
        SimilarityButton.IsEnabled = !string.IsNullOrWhiteSpace(_selected.Source);
        UpdateTranslationProtection(_selected);
        UpdateContextPanel();
        UpdateGlossaryPanel();
        UpdateExactMatchesPanel();
        UpdateValidationPanel();
        UpdateRowNavigationButtons();

        _suppressEditor = false;
    }

    private void ClearEditor()
    {
        _suppressEditor = true;
        SelectedKey.Text = "Строка не выбрана";
        SelectedNamespace.Text = string.Empty;
        SourceText.Text = "—";
        TranslationBox.Text = string.Empty;
        TranslationBox.IsEnabled = false;
        LengthLabel.Text = "0 символов";
        TranslationVariantsButton.IsEnabled = false;
        TranslationHistoryButton.IsEnabled = false;
        SimilarityButton.IsEnabled = false;
        TranslationBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xC7, 0xD3));
        TranslationBox.BorderThickness = new Thickness(1);
        TranslationBox.ToolTip = null;
        TechnicalProtectionText.Visibility = Visibility.Collapsed;
        ContextInfoText.Text = "Выберите строку";
        ContextList.ItemsSource = null;
        GlossaryMatchCountText.Text = "0";
        GlossaryWarningText.Text = "Выберите строку";
        GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
        GlossaryMatchesList.ItemsSource = null;
        ExactMatchesCountText.Text = "0";
        ExactMatchesInfoText.Text = "Выберите строку";
        ExactMatchesConflictCountText.Text = "Конфликтующих строк: 0";
        ExactMatchesConflictCountText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
        ExactMatchesList.ItemsSource = null;
        ApplyExactMatchesButton.IsEnabled = false;
        PreviousRowButton.IsEnabled = false;
        NextRowButton.IsEnabled = false;
        ValidationStatusText.Text = "Выберите строку";
        ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x79, 0x8A));
        FixValidationButton.IsEnabled = false;
        AiFixValidationButton.IsEnabled = false;
        ExplainValidationButton.IsEnabled = false;
        _suppressEditor = false;
    }

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditor || _selected is null || _currentDocument is null) return;

        var before = _selected.Translation;
        var after = TranslationBox.Text;

        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        RecordEditBatch(new EditBatch(
            new List<TranslationEdit>
            {
                new(_currentDocument, _selected, before, after)
            },
            "Изменение перевода"));

        _selected.Translation = after;
        ValidationService.Validate(_selected);
        RefreshGlossaryMismatchState(_selected);
        RefreshConflictState(_selected.Source);
        _currentDocument.IsDirty = true;
        _currentDocument.RefreshComputedProperties();

        LengthLabel.Text = $"{after.Length:N0} символов";
        UpdateTranslationProtection(_selected);
        UpdateGlossaryPanel();
        UpdateExactMatchesPanel();
        UpdateValidationPanel();
        UpdateCounters();
        RefreshFilteredViewPreservingSelection();
        StatusText.Text = $"Есть несохранённые изменения: {_currentDocument.FileName}";
    }

    private void UpdateTranslationProtection(LocalizationEntry? entry)
    {
        if (entry is null || !ValidationService.HasIssueKind(entry, ValidationIssueKind.Technical))
        {
            TranslationBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xC7, 0xD3));
            TranslationBox.BorderThickness = new Thickness(1);
            TranslationBox.ToolTip = null;
            TechnicalProtectionText.Visibility = Visibility.Collapsed;
            return;
        }

        TranslationBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC9, 0x3D, 0x32));
        TranslationBox.BorderThickness = new Thickness(2);
        TranslationBox.ToolTip =
            "Нарушены технические элементы оригинала. Проверьте плейсхолдеры, теги и переносы строк.";
        TechnicalProtectionText.Visibility = Visibility.Visible;
    }

    private void UpdateContextPanel()
    {
        if (_selected is null || _currentDocument is null)
        {
            ContextInfoText.Text = "Выберите строку";
            ContextList.ItemsSource = null;
            return;
        }

        var namespaceEntries = _currentDocument.Entries
            .Where(entry => string.Equals(
                entry.Namespace,
                _selected.Namespace,
                StringComparison.Ordinal))
            .ToList();

        var index = namespaceEntries.IndexOf(_selected);
        if (index < 0)
        {
            ContextInfoText.Text = "Контекст недоступен";
            ContextList.ItemsSource = null;
            return;
        }

        var context = new List<ContextEntryView>();

        for (var offset = -2; offset <= 2; offset++)
        {
            if (offset == 0)
                continue;

            var targetIndex = index + offset;
            if (targetIndex < 0 || targetIndex >= namespaceEntries.Count)
                continue;

            context.Add(new ContextEntryView(
                offset,
                namespaceEntries[targetIndex]));
        }

        ContextInfoText.Text = context.Count == 0
            ? "Соседних строк в этом Namespace нет"
            : $"Соседние строки Namespace: {_selected.Namespace}";
        ContextList.ItemsSource = context;
    }

    private async void SimilarityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_selected.Source))
            return;

        var entry = _selected;
        var documents = _documents.ToList();

        SimilarityButton.IsEnabled = false;
        SimilarityButton.Content = "Поиск похожих строк…";
        StatusText.Text = "Поиск по памяти переводов…";

        try
        {
            var result = await Task.Run(() =>
            {
                var sourceMatches = SimilarityService.FindSourceMatches(
                    entry,
                    documents,
                    _translationMemoryService.Entries);

                var translationMatches = SimilarityService.FindTranslationMatches(
                    entry,
                    documents,
                    _translationMemoryService.Entries);
                return (sourceMatches, translationMatches);
            });

            if (!ReferenceEquals(_selected, entry))
                return;

            var window = new SimilarityWindow(
                entry,
                result.sourceMatches,
                result.translationMatches)
            {
                Owner = this
            };

            if (window.ShowDialog() == true &&
                !string.IsNullOrWhiteSpace(window.SelectedTranslation))
            {
                ApplyCorrectedTranslation(
                    window.SelectedTranslation,
                    "Перевод из памяти переводов");

                StatusText.Text = "Перевод из памяти переводов применён";
            }
            else
            {
                StatusText.Text =
                    $"Похожие оригиналы: {result.sourceMatches.Count:N0} • похожие переводы: {result.translationMatches.Count:N0}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось выполнить поиск похожих строк.\n\n{ex.Message}",
                "Память переводов",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SimilarityButton.Content = "Похожие строки / память переводов";
            SimilarityButton.IsEnabled =
                _selected is not null &&
                !string.IsNullOrWhiteSpace(_selected.Source);
        }
    }

    private IReadOnlyList<GlossaryEntry> GetGlossaryMismatches(LocalizationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Translation))
            return Array.Empty<GlossaryEntry>();

        return _glossaryService
            .FindMatches(entry.Source, entry.Namespace)
            .Where(term =>
                term.IsLocked &&
                !term.IsTranslationAccepted(entry.Translation))
            .ToList();
    }

    private bool TranslationHasGlossaryMismatch(
        string source,
        string entryNamespace,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return false;

        return _glossaryService
            .FindMatches(source, entryNamespace)
            .Any(term =>
                term.IsLocked &&
                !term.IsTranslationAccepted(translation));
    }

    private bool HasGlossaryMismatch(LocalizationEntry entry) =>
        _glossaryMismatchEntries.Contains(entry);

    private void RefreshGlossaryMismatchState(LocalizationEntry entry)
    {
        if (GetGlossaryMismatches(entry).Count > 0)
            _glossaryMismatchEntries.Add(entry);
        else
            _glossaryMismatchEntries.Remove(entry);
    }

    private void RebuildGlossaryMismatchCache()
    {
        _glossaryMismatchEntries.Clear();

        if (_currentDocument is null || _glossaryService.Entries.Count == 0)
            return;

        foreach (var entry in _currentDocument.Entries)
            RefreshGlossaryMismatchState(entry);
    }

    private void GlossaryCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null)
            return;

        RebuildGlossaryMismatchCache();
        UpdateCounters();

        _statusFilter = "Glossary";
        _namespaceFilter = null;
        _validationTypeFilter = ValidationIssueKind.None;

        if (ValidationTypeFilterBox.SelectedIndex != 0)
            ValidationTypeFilterBox.SelectedIndex = 0;

        UpdateFilterVisuals();
        ApplyViewFilter();

        if (_glossaryMismatchEntries.Count == 0)
        {
            StatusText.Text = "Проверка глоссария завершена: нарушений нет";
            MessageBox.Show(
                this,
                "Все закреплённые термины глоссария соблюдены в переведённых строках активного файла.",
                "Проверка глоссария",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var first = _currentDocument.Entries
            .FirstOrDefault(entry => _glossaryMismatchEntries.Contains(entry));

        if (first is not null)
        {
            EntriesGrid.SelectedItem = first;
            EntriesGrid.ScrollIntoView(first);
        }

        StatusText.Text =
            $"Нарушений закреплённого глоссария: {_glossaryMismatchEntries.Count:N0}";

        MessageBox.Show(
            this,
            $"Найдено строк с нарушением закреплённой терминологии: {_glossaryMismatchEntries.Count:N0}.\n\n" +
            "Включён фильтр «Глоссарий».",
            "Проверка глоссария",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void GlossaryConsistencyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null)
            return;

        var rows = BuildGlossaryConsistencyRows();
        if (rows.Count == 0)
        {
            MessageBox.Show(
                this,
                "В активном файле не найдены закреплённые термины из глоссария.",
                "Единообразие терминологии",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new GlossaryConsistencyWindow(rows)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private IReadOnlyList<GlossaryConsistencyRow> BuildGlossaryConsistencyRows()
    {
        if (_currentDocument is null)
            return Array.Empty<GlossaryConsistencyRow>();

        var rows = new List<GlossaryConsistencyRow>();

        foreach (var term in _glossaryService.Entries
                     .Where(term => term.IsLocked)
                     .OrderByDescending(term => term.Priority)
                     .ThenByDescending(term => term.Source.Length)
                     .ThenBy(term => term.Source, StringComparer.Ordinal))
        {
            var hits = _currentDocument.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Translation) &&
                    _glossaryService
                        .FindMatches(entry.Source, entry.Namespace)
                        .Contains(term))
                .ToList();

            if (hits.Count == 0)
                continue;

            var correct = hits.Count(entry =>
                term.IsTranslationAccepted(entry.Translation));

            var mismatches = hits
                .Where(entry =>
                    !term.IsTranslationAccepted(entry.Translation))
                .ToList();

            var samples = string.Join(
                " | ",
                mismatches
                    .Take(3)
                    .Select(entry => $"{entry.Key}: {entry.Translation}"));

            rows.Add(new GlossaryConsistencyRow(
                term.Source,
                term.Translation,
                hits.Count,
                correct,
                mismatches.Count,
                samples));
        }

        return rows
            .OrderByDescending(row => row.MismatchOccurrences)
            .ThenByDescending(row => row.TotalOccurrences)
            .ToList();
    }

    private int GetGlossaryMismatchCount(
        LocalizationEntry entry,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return 0;

        return _glossaryService
            .FindMatches(entry.Source, entry.Namespace)
            .Count(term =>
                term.IsLocked &&
                !term.IsTranslationAccepted(translation));
    }

    private IReadOnlyList<string> GetObservedWrongGlossaryVariants(
        GlossaryEntry term)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in _documents)
        {
            foreach (var entry in document.Entries)
            {
                if (!string.Equals(
                        entry.Source,
                        term.Source,
                        StringComparison.Ordinal) ||
                    !term.AppliesToNamespace(entry.Namespace) ||
                    string.IsNullOrWhiteSpace(entry.Translation) ||
                    term.IsTranslationAccepted(entry.Translation))
                    continue;

                values.Add(entry.Translation.Trim());
            }
        }

        foreach (var memory in _translationMemoryService.Entries)
        {
            if (!string.Equals(
                    memory.Source,
                    term.Source,
                    StringComparison.Ordinal) ||
                !term.AppliesToNamespace(memory.Namespace) ||
                string.IsNullOrWhiteSpace(memory.Translation) ||
                term.IsTranslationAccepted(memory.Translation))
                continue;

            values.Add(memory.Translation.Trim());
        }

        return values
            .OrderByDescending(value => value.Length)
            .ToList();
    }

    private void GlossaryMassLocalFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null || _isMassFixing || _isAiFixing)
            return;

        RebuildGlossaryMismatchCache();

        var document = _currentDocument;
        var proposals = new List<ProposedTranslationChange>();

        foreach (var entry in document.Entries.Where(entry =>
                     _glossaryMismatchEntries.Contains(entry) &&
                     !string.IsNullOrWhiteSpace(entry.Translation)))
        {
            var before = entry.Translation;
            var corrected = before;
            var reasons = new List<string>();
            var beforeMismatchCount = GetGlossaryMismatchCount(entry, before);

            var terms = _glossaryService
                .FindMatches(entry.Source, entry.Namespace)
                .Where(term =>
                    term.IsLocked &&
                    !term.IsTranslationAccepted(corrected))
                .ToList();

            foreach (var term in terms)
            {
                if (string.Equals(
                        entry.Source,
                        term.Source,
                        StringComparison.Ordinal))
                {
                    corrected = term.Translation;
                    reasons.Add($"{term.Source} → {term.Translation}");
                    continue;
                }

                foreach (var wrongVariant in GetObservedWrongGlossaryVariants(term))
                {
                    if (!corrected.Contains(
                            wrongVariant,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    corrected = corrected.Replace(
                        wrongVariant,
                        term.Translation,
                        StringComparison.OrdinalIgnoreCase);

                    reasons.Add(
                        $"{wrongVariant} → {term.Translation}");
                }
            }

            if (string.Equals(
                    corrected,
                    before,
                    StringComparison.Ordinal))
                continue;

            if (ValidationService.GetIssues(
                    entry.Source,
                    corrected).Count > 0)
                continue;

            var afterMismatchCount = GetGlossaryMismatchCount(
                entry,
                corrected);

            if (afterMismatchCount >= beforeMismatchCount)
                continue;

            proposals.Add(new ProposedTranslationChange
            {
                Document = document,
                Entry = entry,
                Before = before,
                After = corrected,
                Reason = reasons.Count == 0
                    ? "Закреплённый термин глоссария"
                    : string.Join("; ", reasons.Distinct())
            });
        }

        if (proposals.Count == 0)
        {
            MessageBox.Show(
                this,
                "Безопасных локальных замен не найдено.\n\n" +
                "Локально исправляются точные строки термина и уже известные варианты перевода. " +
                "Остальные нарушения можно исправить через ChatGPT.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var applied = ApplyProposedChangesWithPreview(
            "Массовое локальное применение глоссария",
            proposals,
            "Массовое локальное исправление глоссария");

        RebuildGlossaryMismatchCache();
        UpdateCounters();
        UpdateButtons();
        RefreshFilteredViewPreservingSelection();

        StatusText.Text = applied == 0
            ? "Локальное исправление глоссария отменено"
            : $"Локально применено терминов: {applied:N0} • осталось нарушений: {_glossaryMismatchEntries.Count:N0}";
    }

    private async void GlossaryMassAiFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null || _isMassFixing || _isAiFixing)
            return;

        RebuildGlossaryMismatchCache();

        var document = _currentDocument;
        var targets = document.Entries
            .Where(entry =>
                _glossaryMismatchEntries.Contains(entry) &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            .ToList();

        if (targets.Count == 0)
        {
            StatusText.Text = "Нарушений закреплённого глоссария нет";
            UpdateCounters();
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"ChatGPT исправит терминологию в {targets.Count:N0} строках файла «{document.FileName}».\n\n" +
            "Для каждой строки выполняется отдельный запрос OpenAI API. " +
            "После обработки откроется предпросмотр, где можно снять отдельные изменения.\n\nПродолжить?",
            "Массовое исправление глоссария",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes || !EnsureOpenAiKey())
            return;

        _isMassFixing = true;
        _isAiFixing = true;
        FilesList.IsEnabled = false;
        EntriesGrid.IsEnabled = false;
        TranslationBox.IsEnabled = false;
        GlossaryMassLocalFixButton.IsEnabled = false;
        GlossaryMassAiFixButton.IsEnabled = false;
        GlossaryMassAiFixButton.Content = "ChatGPT исправляет…";

        var proposals = new List<ProposedTranslationChange>();
        var skipped = 0;
        var failed = 0;
        string? firstError = null;

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var entry = targets[i];

                StatusText.Text =
                    $"Глоссарий ChatGPT: {i + 1:N0}/{targets.Count:N0} — {entry.Key}";
                GlossaryMassAiFixButton.Content =
                    $"ChatGPT {i + 1:N0}/{targets.Count:N0}";

                try
                {
                    var corrected = await _openAiCorrectionService.CorrectAsync(
                        entry,
                        BuildGlossaryContext(entry));

                    if (string.Equals(
                            corrected,
                            entry.Translation,
                            StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }

                    if (ValidationService.GetIssues(
                            entry.Source,
                            corrected).Count > 0 ||
                        TranslationHasGlossaryMismatch(
                            entry.Source,
                            entry.Namespace,
                            corrected))
                    {
                        skipped++;
                        continue;
                    }

                    proposals.Add(new ProposedTranslationChange
                    {
                        Document = document,
                        Entry = entry,
                        Before = entry.Translation,
                        After = corrected,
                        Reason = "ChatGPT: закреплённая терминология"
                    });
                }
                catch (Exception ex)
                {
                    failed++;
                    firstError ??= ex.Message;
                }
            }

            var applied = 0;

            if (proposals.Count > 0)
            {
                applied = ApplyProposedChangesWithPreview(
                    "Массовое исправление глоссария ChatGPT",
                    proposals,
                    "Массовое исправление глоссария ChatGPT");
            }

            RebuildGlossaryMismatchCache();
            UpdateCounters();
            UpdateButtons();
            RefreshFilteredViewPreservingSelection();

            var remaining = _glossaryMismatchEntries.Count;
            var summary =
                $"Запрошено строк: {targets.Count:N0}\n" +
                $"Подготовлено исправлений: {proposals.Count:N0}\n" +
                $"Применено после предпросмотра: {applied:N0}\n" +
                $"Пропущено: {skipped:N0}\n" +
                $"Ошибок API: {failed:N0}\n" +
                $"Нарушений глоссария осталось: {remaining:N0}";

            if (!string.IsNullOrWhiteSpace(firstError))
                summary += $"\n\nПервая ошибка API:\n{firstError}";

            MessageBox.Show(
                this,
                summary,
                "Массовое исправление глоссария",
                MessageBoxButton.OK,
                remaining == 0 && failed == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);

            StatusText.Text =
                $"Глоссарий: применено {applied:N0}, осталось {remaining:N0}";
        }
        finally
        {
            _isAiFixing = false;
            _isMassFixing = false;
            FilesList.IsEnabled = true;
            EntriesGrid.IsEnabled = true;
            TranslationBox.IsEnabled = _selected is not null;
            GlossaryMassAiFixButton.Content = "Исправить с ChatGPT";
            UpdateCounters();
            UpdateButtons();
            UpdateValidationPanel();
        }
    }

    private void GlossaryButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GlossaryWindow(
            _glossaryService,
            _documents.ToList(),
            _currentDocument,
            NavigateToEntryFromGlossary)
        {
            Owner = this
        };

        window.ShowDialog();
        RebuildGlossaryMismatchCache();
        UpdateGlossaryPanel();
        UpdateCounters();
        UpdateButtons();
        RefreshFilteredViewPreservingSelection();
        StatusText.Text = $"Глоссарий: {_glossaryService.Entries.Count:N0} терминов";
    }

    private void NavigateToEntryFromGlossary(
        LocalizationDocument document,
        LocalizationEntry entry)
    {
        if (!ReferenceEquals(_currentDocument, document))
            FilesList.SelectedItem = document;

        _statusFilter = "All";
        _namespaceFilter = null;
        _validationTypeFilter = ValidationIssueKind.None;

        if (ValidationTypeFilterBox.SelectedIndex != 0)
            ValidationTypeFilterBox.SelectedIndex = 0;

        SearchBox.Clear();
        UpdateFilterVisuals();
        ApplyViewFilter();

        EntriesGrid.SelectedItem = entry;
        EntriesGrid.ScrollIntoView(entry);
        EntriesGrid.Focus();

        StatusText.Text =
            $"Открыто использование термина: {document.FileName} • {entry.Namespace} • {entry.Key}";
    }

    private void UpdateGlossaryPanel()
    {
        if (_selected is null)
        {
            GlossaryMatchCountText.Text = "0";
            GlossaryWarningText.Text = "Выберите строку";
            GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            GlossaryMatchesList.ItemsSource = null;
            return;
        }

        var matches = _glossaryService.FindMatches(_selected.Source, _selected.Namespace);
        GlossaryMatchCountText.Text = matches.Count.ToString("N0");
        GlossaryMatchesList.ItemsSource = matches;

        if (matches.Count == 0)
        {
            GlossaryWarningText.Text = "Совпадений с глоссарием нет";
            GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            return;
        }

        if (string.IsNullOrWhiteSpace(_selected.Translation))
        {
            GlossaryWarningText.Text =
                $"Найдено терминов: {matches.Count:N0}. Они будут проверены после ввода перевода.";
            GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0x52, 0x65, 0x79));
            return;
        }

        var mismatches = matches
            .Where(term =>
                term.IsLocked &&
                !term.IsTranslationAccepted(_selected.Translation))
            .ToList();

        if (mismatches.Count == 0)
        {
            GlossaryWarningText.Text =
                $"✓ Найдено терминов: {matches.Count:N0}. Закреплённая терминология соблюдена.";
            GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x75, 0x40));
            return;
        }

        var preview = string.Join(
            "; ",
            mismatches
                .Take(4)
                .Select(term => $"{term.Source} → {term.Translation}"));

        if (mismatches.Count > 4)
            preview += $" и ещё {mismatches.Count - 4:N0}";

        GlossaryWarningText.Text =
            $"⚠ Не соблюдены закреплённые термины: {preview}";
        GlossaryWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
    }

    private string BuildGlossaryContext(LocalizationEntry entry)
    {
        var matches = _glossaryService.FindMatches(entry.Source, entry.Namespace);
        if (matches.Count == 0)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            matches.Select(term =>
                $"{(term.IsLocked ? "[ОБЯЗАТЕЛЬНО]" : "[ПОДСКАЗКА]")} " +
                $"{term.Source} => {term.Translation}" +
                (term.AllowedTranslations.Count == 0
                    ? string.Empty
                    : $" | допустимо: {string.Join(", ", term.AllowedTranslations)}") +
                (string.IsNullOrWhiteSpace(term.Note) ? string.Empty : $" ({term.Note})")));
    }

    private void UpdateExactMatchesPanel()
    {
        if (_selected is null || string.IsNullOrEmpty(_selected.Source))
        {
            ExactMatchesCountText.Text = "0";
            ExactMatchesInfoText.Text = "Точные совпадения не найдены";
            ExactMatchesInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            ExactMatchesConflictCountText.Text = "Конфликтующих строк: 0";
            ExactMatchesConflictCountText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            ExactMatchesList.ItemsSource = null;
            ApplyExactMatchesButton.IsEnabled = false;
            return;
        }

        var matches = GetExactSourceMatches(_selected);
        var otherMatches = Math.Max(0, matches.Count - 1);

        ExactMatchesCountText.Text = matches.Count.ToString("N0");
        ExactMatchesList.ItemsSource = matches;

        if (otherMatches == 0)
        {
            ExactMatchesInfoText.Text = "Других строк с полностью идентичным оригиналом нет.";
            ExactMatchesInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            var variantCount = string.IsNullOrWhiteSpace(_selected.Translation) ? 0 : 1;
            ExactMatchesConflictCountText.Text =
                $"Конфликтующих строк: 0  •  вариантов перевода: {variantCount:N0}";
            ExactMatchesConflictCountText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7D, 0x8E));
            ApplyExactMatchesButton.IsEnabled = false;
            return;
        }

        var translatedMatches = matches
            .Where(item => !string.IsNullOrWhiteSpace(item.Entry.Translation))
            .ToList();

        var distinctTranslations = translatedMatches
            .Select(item => item.Entry.Translation)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var conflictingRows = distinctTranslations.Count > 1
            ? translatedMatches.Count
            : 0;

        ExactMatchesConflictCountText.Text =
            $"Конфликтующих строк: {conflictingRows:N0}  •  вариантов перевода: {distinctTranslations.Count:N0}";
        ExactMatchesConflictCountText.Foreground = new SolidColorBrush(
            conflictingRows > 0
                ? Color.FromRgb(0xB3, 0x3A, 0x2B)
                : Color.FromRgb(0x6B, 0x7D, 0x8E));

        var fileCount = matches
            .Select(item => item.Document.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctTranslations.Count > 1)
        {
            ExactMatchesInfoText.Text =
                $"⚠ Найдено разных вариантов перевода: {distinctTranslations.Count}. " +
                $"100% совпадений: {matches.Count:N0} в {fileCount:N0} открытых файлах.";
            ExactMatchesInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
        }
        else
        {
            ExactMatchesInfoText.Text =
                $"Найдено ещё {otherMatches:N0} строк с 100% идентичным оригиналом " +
                $"в {fileCount:N0} открытых файлах.";
            ExactMatchesInfoText.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x75, 0x40));
        }

        ApplyExactMatchesButton.IsEnabled = !string.IsNullOrWhiteSpace(_selected.Translation);
    }

    private void ExactMatchItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not EntryLocation match)
            return;

        _statusFilter = "All";
        _namespaceFilter = null;
        _validationTypeFilter = ValidationIssueKind.None;
        if (ValidationTypeFilterBox.SelectedIndex != 0)
            ValidationTypeFilterBox.SelectedIndex = 0;
        SearchBox.Clear();
        UpdateFilterVisuals();

        if (!ReferenceEquals(_currentDocument, match.Document))
            FilesList.SelectedItem = match.Document;
        else
            ApplyViewFilter();

        EntriesGrid.SelectedItem = match.Entry;
        EntriesGrid.ScrollIntoView(match.Entry);
        EntriesGrid.Focus();

        StatusText.Text =
            $"Открыто точное совпадение: {match.Document.FileName} • {match.Entry.Namespace} • {match.Entry.Key}";

        e.Handled = true;
    }

    private void PreviousRowButton_Click(object sender, RoutedEventArgs e) =>
        NavigateVisibleRow(-1);

    private void NextRowButton_Click(object sender, RoutedEventArgs e) =>
        NavigateVisibleRow(1);

    private void NavigateVisibleRow(int delta)
    {
        var currentIndex = EntriesGrid.SelectedIndex;
        if (currentIndex < 0)
            return;

        var targetIndex = currentIndex + delta;
        if (targetIndex < 0 || targetIndex >= EntriesGrid.Items.Count)
            return;

        EntriesGrid.SelectedIndex = targetIndex;

        if (EntriesGrid.SelectedItem is LocalizationEntry target)
        {
            EntriesGrid.ScrollIntoView(target);
            EntriesGrid.Focus();
        }
    }

    private void UpdateRowNavigationButtons()
    {
        var currentIndex = EntriesGrid.SelectedIndex;
        var count = EntriesGrid.Items.Count;

        PreviousRowButton.IsEnabled = currentIndex > 0;
        NextRowButton.IsEnabled = currentIndex >= 0 && currentIndex < count - 1;
    }

    private void ApplyExactMatchesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_selected.Translation))
            return;

        var translation = _selected.Translation;
        var matches = GetExactSourceMatches(_selected);

        var proposals = matches
            .Where(item =>
                !string.Equals(
                    item.Entry.Translation,
                    translation,
                    StringComparison.Ordinal))
            .Select(item => new ProposedTranslationChange
            {
                Document = item.Document,
                Entry = item.Entry,
                Before = item.Entry.Translation,
                After = translation,
                Reason = "100% идентичный китайский оригинал"
            })
            .ToList();

        if (proposals.Count == 0)
        {
            StatusText.Text =
                "Все точные совпадения уже имеют этот перевод";
            UpdateExactMatchesPanel();
            return;
        }

        var applied = ApplyProposedChangesWithPreview(
            "Применить к точным совпадениям",
            proposals,
            "Применение к точным совпадениям");

        if (applied == 0)
        {
            StatusText.Text =
                "Применение к точным совпадениям отменено";
            return;
        }

        var affectedFiles = proposals
            .Where(change => change.IsSelected)
            .Select(change => change.Document.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        StatusText.Text =
            $"Перевод применён к {applied:N0} строкам с 100% совпадением в {affectedFiles:N0} файлах";
    }

    private int ApplyProposedChangesWithPreview(
        string title,
        IReadOnlyList<ProposedTranslationChange> changes,
        string historyDescription)
    {
        if (changes.Count == 0)
            return 0;

        var window = new MassChangePreviewWindow(title, changes)
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
            return 0;

        var selected = window.SelectedChanges;
        if (selected.Count == 0)
            return 0;

        var edits = selected
            .Where(change =>
                !string.Equals(
                    change.Before,
                    change.After,
                    StringComparison.Ordinal))
            .Select(change => new TranslationEdit(
                change.Document,
                change.Entry,
                change.Before,
                change.After))
            .ToList();

        if (edits.Count == 0)
            return 0;

        var batch = new EditBatch(edits, historyDescription);
        RecordEditBatch(batch);
        ApplyEditBatch(batch, useAfter: true);

        return edits.Count;
    }

    private void RecordEditBatch(EditBatch batch)
    {
        if (batch.Edits.Count == 0)
            return;

        _undoStack.Push(batch);
        _redoStack.Clear();
    }

    private void ApplyEditBatch(EditBatch batch, bool useAfter)
    {
        var affectedDocuments = new HashSet<LocalizationDocument>();
        var affectedSources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edit in batch.Edits)
        {
            var value = useAfter ? edit.After : edit.Before;

            if (!string.Equals(edit.Entry.Translation, value, StringComparison.Ordinal))
                edit.Entry.Translation = value;

            ValidationService.Validate(edit.Entry);
            if (ReferenceEquals(edit.Document, _currentDocument))
                RefreshGlossaryMismatchState(edit.Entry);
            edit.Document.IsDirty = true;
            affectedDocuments.Add(edit.Document);

            if (!string.IsNullOrEmpty(edit.Entry.Source))
                affectedSources.Add(edit.Entry.Source);
        }

        foreach (var source in affectedSources)
            RefreshConflictState(source);

        foreach (var document in affectedDocuments)
            document.RefreshComputedProperties();

        if (_selected is not null)
        {
            _suppressEditor = true;
            TranslationBox.Text = _selected.Translation;
            LengthLabel.Text = $"{_selected.Translation.Length:N0} символов";
            _suppressEditor = false;

            UpdateTranslationProtection(_selected);
            UpdateContextPanel();
            UpdateGlossaryPanel();
            UpdateExactMatchesPanel();
            UpdateValidationPanel();
        }

        RefreshFilteredViewPreservingSelection();
        UpdateCounters();
    }

    private void UndoLastEdit()
    {
        if (_undoStack.Count == 0)
        {
            StatusText.Text = "Нет изменений для отмены";
            return;
        }

        var batch = _undoStack.Pop();
        ApplyEditBatch(batch, useAfter: false);
        _redoStack.Push(batch);
        StatusText.Text = $"Отменено: {batch.Description}";
    }

    private void RedoLastEdit()
    {
        if (_redoStack.Count == 0)
        {
            StatusText.Text = "Нет изменений для повтора";
            return;
        }

        var batch = _redoStack.Pop();
        ApplyEditBatch(batch, useAfter: true);
        _undoStack.Push(batch);
        StatusText.Text = $"Повторено: {batch.Description}";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox focusedTextBox &&
            !ReferenceEquals(focusedTextBox, TranslationBox))
            return;

        if (e.Key == Key.Z)
        {
            UndoLastEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            RedoLastEdit();
            e.Handled = true;
        }
    }

    private void UpdateValidationPanel()
    {
        var canFix =
            !_isAiFixing &&
            _selected is not null &&
            !string.IsNullOrWhiteSpace(_selected.Translation) &&
            _selected.HasValidationIssues;

        FixValidationButton.IsEnabled = canFix;
        AiFixValidationButton.IsEnabled = canFix;
        ExplainValidationButton.IsEnabled = canFix;
        TranslationVariantsButton.IsEnabled =
            !_isAiFixing &&
            _selected is not null &&
            !string.IsNullOrWhiteSpace(_selected.Source);

        if (_selected is null)
        {
            ValidationStatusText.Text = "Выберите строку";
            ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x79, 0x8A));
            return;
        }

        if (string.IsNullOrWhiteSpace(_selected.Translation))
        {
            ValidationStatusText.Text = "— Строка не переведена. Проверка будет выполнена после ввода перевода.";
            ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x79, 0x8A));
            return;
        }

        if (_selected.HasValidationIssues)
        {
            ValidationStatusText.Text = $"⚠ {_selected.ValidationSummary}";
            ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
            return;
        }

        ValidationStatusText.Text = "✓ Строка прошла все проверки.";
        ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x75, 0x40));
    }

    private void FixValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _currentDocument is null || !_selected.HasValidationIssues)
            return;

        var entry = _selected;

        try
        {
            var corrected = ValidationService.AutoFixDeterministic(
                entry.Source,
                entry.Translation);

            if (string.Equals(corrected, entry.Translation, StringComparison.Ordinal))
            {
                StatusText.Text = "Эту проблему нельзя безопасно исправить автоматически";
                return;
            }

            ApplyCorrectedTranslation(corrected, "Автоисправление проверки");

            // После исправления строка может исчезнуть из активного фильтра ошибок,
            // поэтому используем сохранённую ссылку entry, а не _selected.
            StatusText.Text = entry.HasValidationIssues
                ? "Автоисправление применено; оставшиеся проблемы требуют проверки"
                : "Ошибки строки исправлены";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось исправить строку.\n\n{ex.Message}",
                "Ошибка автоисправления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "Ошибка автоисправления — изменения не применены";
        }
    }

    private void MassLocalFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null || _isMassFixing || _isAiFixing)
            return;

        ValidationService.ValidateAll(_currentDocument.Entries);

        var targets = _currentDocument.Entries
            .Where(entry =>
                entry.HasValidationIssues &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            .ToList();

        if (targets.Count == 0)
        {
            StatusText.Text = "Нет ошибок, которые можно исправить";
            UpdateCounters();
            return;
        }

        var proposals = new List<ProposedTranslationChange>();

        foreach (var entry in targets)
        {
            var corrected = ValidationService.AutoFixDeterministic(
                entry.Source,
                entry.Translation);

            if (string.Equals(
                    corrected,
                    entry.Translation,
                    StringComparison.Ordinal))
                continue;

            proposals.Add(new ProposedTranslationChange
            {
                Document = _currentDocument,
                Entry = entry,
                Before = entry.Translation,
                After = corrected,
                Reason = "Безопасное локальное автоисправление"
            });
        }

        if (proposals.Count == 0)
        {
            MassFixStatusText.Text =
                $"Локально исправить нечего • осталось ошибок: {targets.Count:N0}";
            StatusText.Text =
                "Автоматически исправляемых ошибок не найдено";
            return;
        }

        var applied = ApplyProposedChangesWithPreview(
            "Массовое локальное исправление",
            proposals,
            "Массовое локальное исправление");

        if (applied == 0)
        {
            StatusText.Text = "Массовое локальное исправление отменено";
            return;
        }

        var remaining = _currentDocument.Entries.Count(entry => entry.HasValidationIssues);
        var fullyFixed = proposals.Count(change => !change.Entry.HasValidationIssues);

        MassFixStatusText.Text =
            $"Локально изменено: {applied:N0} • полностью исправлено: {fullyFixed:N0} • осталось: {remaining:N0}";

        StatusText.Text =
            $"Массовое локальное исправление: применено {applied:N0}, осталось ошибок {remaining:N0}";
    }

    private async void MassAiFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null || _isMassFixing || _isAiFixing)
            return;

        ValidationService.ValidateAll(_currentDocument.Entries);

        var document = _currentDocument;
        var targets = document.Entries
            .Where(entry =>
                entry.HasValidationIssues &&
                !string.IsNullOrWhiteSpace(entry.Translation))
            .ToList();

        if (targets.Count == 0)
        {
            StatusText.Text = "Нет ошибок для исправления ChatGPT";
            UpdateCounters();
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"ChatGPT обработает {targets.Count:N0} проблемных строк файла «{document.FileName}».\n\n" +
            "Для каждой строки выполняется отдельный запрос OpenAI API. " +
            "Это может занять время и расходует API-баланс. После обработки откроется предпросмотр всех изменений.\n\nПродолжить?",
            "Массовое исправление ChatGPT",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes || !EnsureOpenAiKey())
            return;

        _isMassFixing = true;
        _isAiFixing = true;

        FilesList.IsEnabled = false;
        EntriesGrid.IsEnabled = false;
        TranslationBox.IsEnabled = false;
        MassLocalFixButton.IsEnabled = false;
        MassAiFixButton.IsEnabled = false;
        ValidationButton.IsEnabled = false;

        var proposals = new List<ProposedTranslationChange>();
        var skipped = 0;
        var failed = 0;
        string? firstError = null;

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var entry = targets[i];
                var current = i + 1;

                MassAiFixButton.Content = $"ChatGPT {current:N0}/{targets.Count:N0}";
                MassFixStatusText.Text =
                    $"ChatGPT: {current:N0}/{targets.Count:N0} • готово: {proposals.Count:N0} • пропущено: {skipped + failed:N0}";
                StatusText.Text =
                    $"Массовое исправление ChatGPT: {current:N0} из {targets.Count:N0} — {entry.Key}";

                try
                {
                    var corrected = await _openAiCorrectionService.CorrectAsync(
                        entry,
                        BuildGlossaryContext(entry));

                    if (string.Equals(
                            corrected,
                            entry.Translation,
                            StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }

                    var remainingIssues = ValidationService.GetIssues(
                        entry.Source,
                        corrected);

                    if (remainingIssues.Count > 0 ||
                        TranslationHasGlossaryMismatch(
                            entry.Source,
                            entry.Namespace,
                            corrected))
                    {
                        skipped++;
                        continue;
                    }

                    proposals.Add(new ProposedTranslationChange
                    {
                        Document = document,
                        Entry = entry,
                        Before = entry.Translation,
                        After = corrected,
                        Reason = "ChatGPT: исправление ошибки"
                    });
                }
                catch (Exception ex)
                {
                    failed++;
                    firstError ??= ex.Message;
                }
            }

            var applied = 0;

            if (proposals.Count > 0)
            {
                FilesList.IsEnabled = true;
                EntriesGrid.IsEnabled = true;

                applied = ApplyProposedChangesWithPreview(
                    "Массовое исправление ChatGPT",
                    proposals,
                    "Массовое исправление ChatGPT");

                FilesList.IsEnabled = false;
                EntriesGrid.IsEnabled = false;
            }

            ValidationService.ValidateAll(document.Entries);
            document.RefreshComputedProperties();
            RebuildGlossaryMismatchCache();

            var remaining = document.Entries.Count(entry => entry.HasValidationIssues);

            MassFixStatusText.Text =
                $"ChatGPT применено: {applied:N0} • пропущено: {skipped:N0} • ошибок API: {failed:N0} • осталось: {remaining:N0}";

            StatusText.Text =
                $"ChatGPT: применено {applied:N0}, осталось ошибок {remaining:N0}";

            var summary =
                $"Запрошено строк: {targets.Count:N0}\n" +
                $"Подготовлено исправлений: {proposals.Count:N0}\n" +
                $"Применено после предпросмотра: {applied:N0}\n" +
                $"Пропущено из-за оставшихся ошибок/без изменений: {skipped:N0}\n" +
                $"Ошибок API: {failed:N0}\n" +
                $"Ошибок в файле осталось: {remaining:N0}";

            if (!string.IsNullOrWhiteSpace(firstError))
                summary += $"\n\nПервая ошибка API:\n{firstError}";

            MessageBox.Show(
                this,
                summary,
                "Массовое исправление ChatGPT",
                MessageBoxButton.OK,
                remaining == 0 && failed == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        finally
        {
            _isAiFixing = false;
            _isMassFixing = false;

            MassAiFixButton.Content = "Исправить все с ChatGPT";
            FilesList.IsEnabled = true;
            EntriesGrid.IsEnabled = true;
            TranslationBox.IsEnabled = _selected is not null;

            UpdateValidationPanel();
            UpdateCounters();
            UpdateButtons();
            RefreshFilteredViewPreservingSelection();
        }
    }

    private bool EnsureOpenAiKey()
    {
        if (_openAiCorrectionService.HasApiKey)
            return true;

        var keyWindow = new OpenAiKeyWindow { Owner = this };
        if (keyWindow.ShowDialog() != true)
            return false;

        try
        {
            _openAiCorrectionService.SetApiKey(keyWindow.ApiKey, keyWindow.Remember);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async void AiFixValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _currentDocument is null || !_selected.HasValidationIssues || _isAiFixing)
            return;

        if (!EnsureOpenAiKey())
            return;

        var entry = _selected;
        _isAiFixing = true;
        AiFixValidationButton.Content = "ChatGPT исправляет…";
        UpdateValidationPanel();
        StatusText.Text = "ChatGPT исправляет строку…";

        try
        {
            var corrected = await _openAiCorrectionService.CorrectAsync(
                        entry,
                        BuildGlossaryContext(entry));

            if (!ReferenceEquals(_selected, entry))
            {
                StatusText.Text = "Строка изменилась во время запроса — ответ ChatGPT не применён";
                return;
            }

            var issues = ValidationService.GetIssues(entry.Source, corrected);
            var critical = issues.Where(issue => issue.IsCritical).ToList();
            if (critical.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "ChatGPT вернул вариант с критическими техническими ошибками:\n\n" +
                    string.Join(Environment.NewLine, critical.Select(issue => issue.Message)),
                    "Умное исправление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusText.Text = "Ответ ChatGPT не применён: остались критические ошибки";
                return;
            }

            var preview = new AiCorrectionPreviewWindow(
                entry.Source,
                entry.Translation,
                corrected,
                string.Join(Environment.NewLine, issues.Select(issue => issue.Message)))
            {
                Owner = this
            };

            if (preview.ShowDialog() != true)
            {
                StatusText.Text = "Исправление ChatGPT отменено";
                return;
            }

            ApplyCorrectedTranslation(corrected, "Умное исправление ChatGPT");
            StatusText.Text = "Исправление ChatGPT применено";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ошибка ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Не удалось выполнить умное исправление";
        }
        finally
        {
            _isAiFixing = false;
            AiFixValidationButton.Content = "Умное исправление с ChatGPT";
            UpdateValidationPanel();
        }
    }

    private async void ExplainValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !_selected.HasValidationIssues || _isAiFixing)
            return;

        if (!EnsureOpenAiKey())
            return;

        var entry = _selected;
        _isAiFixing = true;
        ExplainValidationButton.Content = "ChatGPT анализирует…";
        UpdateValidationPanel();
        StatusText.Text = "ChatGPT анализирует ошибку…";

        try
        {
            var explanation = await _openAiCorrectionService.ExplainAsync(
                entry,
                BuildGlossaryContext(entry));

            if (!ReferenceEquals(_selected, entry))
                return;

            MessageBox.Show(
                this,
                explanation,
                "Объяснение ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text = "Объяснение получено";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ошибка ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Не удалось получить объяснение";
        }
        finally
        {
            _isAiFixing = false;
            ExplainValidationButton.Content = "Объяснить с ChatGPT";
            UpdateValidationPanel();
        }
    }

    private async void TranslationVariantsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _currentDocument is null || _isAiFixing)
            return;

        if (!EnsureOpenAiKey())
            return;

        var entry = _selected;
        _isAiFixing = true;
        TranslationVariantsButton.Content = "ChatGPT переводит…";
        UpdateValidationPanel();
        StatusText.Text = "ChatGPT готовит варианты перевода…";

        try
        {
            var variants = await _openAiCorrectionService.SuggestVariantsAsync(
                entry,
                BuildGlossaryContext(entry),
                3);

            if (!ReferenceEquals(_selected, entry))
                return;

            var safeVariants = variants
                .Where(variant => !ValidationService.GetIssues(entry.Source, variant).Any(issue => issue.IsCritical))
                .ToList();

            if (safeVariants.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "ChatGPT вернул варианты с ошибками технических элементов. Они не будут предложены.",
                    "Варианты перевода",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new TranslationVariantsWindow(entry.Source, safeVariants)
            {
                Owner = this
            };

            if (window.ShowDialog() == true &&
                !string.IsNullOrWhiteSpace(window.SelectedTranslation))
            {
                ApplyCorrectedTranslation(window.SelectedTranslation, "Вариант перевода ChatGPT");
                StatusText.Text = "Выбранный вариант перевода применён";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ошибка ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Не удалось получить варианты перевода";
        }
        finally
        {
            _isAiFixing = false;
            TranslationVariantsButton.Content = "Варианты ChatGPT";
            UpdateValidationPanel();
        }
    }

    private async void TranslationHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _currentDocument is null)
            return;

        try
        {
            var records = await _translationHistoryService.LoadForEntryAsync(
                _currentDocument,
                _selected);

            var window = new TranslationHistoryWindow(
                _currentDocument.FileName,
                _selected.Namespace,
                _selected.Key,
                records)
            {
                Owner = this
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть историю изменений.\n\n{ex.Message}",
                "История изменений",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplyCorrectedTranslation(string corrected, string description)
    {
        if (_selected is null || _currentDocument is null)
            return;

        if (string.Equals(_selected.Translation, corrected, StringComparison.Ordinal))
            return;

        var batch = new EditBatch(
            new List<TranslationEdit>
            {
                new(_currentDocument, _selected, _selected.Translation, corrected)
            },
            description);

        RecordEditBatch(batch);
        ApplyEditBatch(batch, useAfter: true);
        RefreshFilteredViewPreservingSelection();
    }

    private void UpdateCounters()
    {
        if (_currentDocument is null)
        {
            TotalBadge.Text = "Все  0";
            TranslatedBadge.Text = "Переведено  0";
            UntranslatedBadge.Text = "Не переведено  0";
            ErrorBadge.Text = "Ошибки  0";
            ConflictBadge.Text = "Конфликты  0";
            GlossaryBadge.Text = "Глоссарий  0";
            ModifiedBadge.Text = "Изменённые  0";
            QualityStatsText.Text = "Файл не открыт";
            MassLocalFixButton.IsEnabled = false;
            MassAiFixButton.IsEnabled = false;
            MassFixStatusText.Text = "Исправляет все ошибки активного файла";
            return;
        }

        var total = _currentDocument.Entries.Count;
        var translated = 0;
        var errors = 0;
        var conflicts = 0;
        var modified = 0;

        foreach (var entry in _currentDocument.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Translation))
                translated++;
            if (entry.HasValidationIssues)
                errors++;
            if (HasTranslationConflict(entry))
                conflicts++;
            if (entry.IsModified)
                modified++;
        }

        var untranslated = total - translated;

        TotalBadge.Text = $"Все  {total:N0}";
        TranslatedBadge.Text = $"Переведено  {translated:N0}";
        UntranslatedBadge.Text = $"Не переведено  {untranslated:N0}";
        ErrorBadge.Text = $"Ошибки  {errors:N0}";
        var glossaryMismatches = _glossaryMismatchEntries.Count;
        ConflictBadge.Text = $"Конфликты  {conflicts:N0}";
        GlossaryBadge.Text = $"Глоссарий  {glossaryMismatches:N0}";
        ModifiedBadge.Text = $"Изменённые  {modified:N0}";

        var translatedPercent = total == 0 ? 0 : translated * 100.0 / total;
        QualityStatsText.Text =
            $"Переведено: {translated:N0}/{total:N0} ({translatedPercent:0.0}%)  •  " +
            $"Пустых: {untranslated:N0}  •  Ошибок: {errors:N0}  •  " +
            $"Глоссарий: {glossaryMismatches:N0}  •  Конфликты: {conflicts:N0}";

        var canMassFix = errors > 0 && !_isAiFixing && !_isMassFixing;
        MassLocalFixButton.IsEnabled = canMassFix;
        MassAiFixButton.IsEnabled = canMassFix;
        MassFixStatusText.Text = errors == 0
            ? "Ошибок в активном файле нет"
            : $"Ошибок в активном файле: {errors:N0}";

        GlossaryCheckButton.IsEnabled = _glossaryService.Entries.Count > 0;
        GlossaryConsistencyButton.IsEnabled = _glossaryService.Entries.Any(entry => entry.IsLocked);
        GlossaryMassLocalFixButton.IsEnabled =
            glossaryMismatches > 0 &&
            !_isAiFixing &&
            !_isMassFixing;
        GlossaryMassAiFixButton.IsEnabled =
            glossaryMismatches > 0 &&
            !_isAiFixing &&
            !_isMassFixing;
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null) return;

        StatusText.Text = $"Проверка: {_currentDocument.FileName}…";
        ValidationService.ValidateAll(_currentDocument.Entries);
        _currentDocument.RefreshComputedProperties();
        EntriesGrid.Items.Refresh();
        RefreshFilteredViewPreservingSelection();
        UpdateCounters();

        var entries = _currentDocument.Entries;
        var errorLines = entries.Count(entry => entry.HasValidationIssues);
        var critical = entries.Count(entry => entry.HasCriticalValidationIssues);
        var technical = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.Technical));
        var chinese = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.ChineseText));
        var brackets = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.Brackets));
        var whitespace = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.Whitespace));
        var punctuation = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.Punctuation));
        var sourceCopy = entries.Count(entry => ValidationService.HasIssueKind(entry, ValidationIssueKind.SourceCopy));

        var report =
            $"Проверено строк: {entries.Count:N0}\n" +
            $"Строк с проблемами: {errorLines:N0}\n" +
            $"Критические: {critical:N0}\n\n" +
            $"Технические элементы: {technical:N0}\n" +
            $"Китайский текст: {chinese:N0}\n" +
            $"Скобки: {brackets:N0}\n" +
            $"Пробелы: {whitespace:N0}\n" +
            $"Пунктуация: {punctuation:N0}\n" +
            $"Копия оригинала: {sourceCopy:N0}";

        MessageBox.Show(
            this,
            report,
            $"Проверка — {_currentDocument.FileName}",
            MessageBoxButton.OK,
            errorLines == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

        var firstError = entries.FirstOrDefault(entry => entry.HasValidationIssues);
        if (firstError is null)
        {
            StatusText.Text = $"Проверка завершена: ошибок нет — {_currentDocument.FileName}";
        }
        else
        {
            EntriesGrid.SelectedItem = firstError;
            EntriesGrid.ScrollIntoView(firstError);
            StatusText.Text = $"Проверка завершена: {errorLines:N0} проблем — {_currentDocument.FileName}";
        }
    }

    private async void SaveCurrentButton_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();
    private async void SaveAllButton_Click(object sender, RoutedEventArgs e) => await SaveAllAsync();

    private async Task SaveCurrentAsync()
    {
        if (_currentDocument is null) return;
        await SaveDocumentAsync(_currentDocument);
    }

    private async Task SaveAllAsync()
    {
        if (_documents.Count == 0) return;

        var dirty = _documents.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusText.Text = "Нет несохранённых изменений";
            return;
        }

        foreach (var document in dirty)
        {
            if (!await SaveDocumentAsync(document, showStatus: false))
                return;
        }

        StatusText.Text = $"Сохранено файлов: {dirty.Count}";
    }

    private QualityReport BuildQualityReport(LocalizationDocument document)
    {
        ValidationService.ValidateAll(document.Entries);

        var total = document.Entries.Count;
        var translated = document.Entries.Count(entry =>
            !string.IsNullOrWhiteSpace(entry.Translation));
        var untranslated = total - translated;

        var validationErrors = document.Entries.Count(entry =>
            entry.HasValidationIssues);
        var critical = document.Entries.Count(entry =>
            entry.HasCriticalValidationIssues);
        var chinese = document.Entries.Count(entry =>
            ValidationService.HasIssueKind(
                entry,
                ValidationIssueKind.ChineseText));
        var conflicts = document.Entries.Count(HasTranslationConflict);
        var glossaryMismatches = document.Entries.Count(entry =>
            GetGlossaryMismatches(entry).Count > 0);
        var glossaryDefinitionConflicts = _glossaryService.GetConflicts().Count;

        return new QualityReport(
            total,
            translated,
            untranslated,
            validationErrors,
            critical,
            chinese,
            conflicts,
            glossaryMismatches,
            glossaryDefinitionConflicts);
    }

    private async Task<bool> SaveDocumentAsync(
        LocalizationDocument document,
        bool showStatus = true)
    {
        try
        {
            var quality = BuildQualityReport(document);

            if (quality.HasWarnings)
            {
                var result = MessageBox.Show(
                    this,
                    $"Контроль качества перед сохранением «{document.FileName}»:\n\n" +
                    $"Всего строк: {quality.Total:N0}\n" +
                    $"Переведено: {quality.Translated:N0}\n" +
                    $"Пустых: {quality.Untranslated:N0}\n\n" +
                    $"Ошибки проверки: {quality.ValidationErrors:N0}\n" +
                    $"Критические технические: {quality.CriticalErrors:N0}\n" +
                    $"Остался китайский текст: {quality.ChineseText:N0}\n" +
                    $"Конфликты одинаковых source: {quality.SourceConflicts:N0}\n" +
                    $"Нарушения закреплённого глоссария: {quality.GlossaryMismatches:N0}\n" +
                    $"Конфликты внутри глоссария: {quality.GlossaryDefinitionConflicts:N0}\n\n" +
                    "Сохранение не блокируется. Сохранить файл с этими замечаниями?",
                    "Контроль качества",
                    MessageBoxButton.YesNo,
                    quality.CriticalErrors > 0
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    StatusText.Text =
                        $"Сохранение отменено после контроля качества: {document.FileName}";
                    return false;
                }
            }

            var changedEntries = document.Entries
                .Where(entry => !string.Equals(
                    entry.LastSavedTranslation,
                    entry.Translation,
                    StringComparison.Ordinal))
                .ToList();

            if (showStatus)
                StatusText.Text = $"Сохранение: {document.FileName}…";

            await _service.SaveAsync(
                document.FilePath,
                document.Entries);

            try
            {
                await _translationHistoryService.AppendSavedChangesAsync(
                    document,
                    changedEntries);

                await _translationMemoryService.RememberDocumentAsync(
                    document);
            }
            catch
            {
                // Ошибка локальной истории/памяти не должна отменять уже успешное сохранение локализации.
            }

            foreach (var entry in document.Entries)
                entry.MarkSaved();

            document.IsDirty = false;
            document.RefreshComputedProperties();

            if (ReferenceEquals(document, _currentDocument))
            {
                RebuildGlossaryMismatchCache();
                EntriesGrid.Items.Refresh();
                RefreshFilteredViewPreservingSelection();
                UpdateCounters();
                UpdateButtons();
            }

            if (showStatus)
            {
                StatusText.Text = quality.HasWarnings
                    ? $"Сохранено с замечаниями контроля качества: {document.FileName}"
                    : $"Сохранено: {document.FileName} • контроль качества пройден";
            }

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось сохранить {document.FileName}.\n\n{ex.Message}",
                "Ошибка сохранения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                $"Ошибка сохранения: {document.FileName}";
            return false;
        }
    }

    private void UpdateButtons()
    {
        var hasDocuments = _documents.Count > 0;
        var hasCurrent = _currentDocument is not null;
        SaveCurrentButton.IsEnabled = hasCurrent;
        SaveAllButton.IsEnabled = hasDocuments;
        ValidationButton.IsEnabled = hasCurrent;
        GlossaryCheckButton.IsEnabled = hasCurrent && _glossaryService.Entries.Count > 0;
        GlossaryConsistencyButton.IsEnabled = hasCurrent && _glossaryService.Entries.Any(entry => entry.IsLocked);
        GlossaryMassLocalFixButton.IsEnabled =
            hasCurrent &&
            _glossaryMismatchEntries.Count > 0 &&
            !_isAiFixing &&
            !_isMassFixing;
        GlossaryMassAiFixButton.IsEnabled =
            hasCurrent &&
            _glossaryMismatchEntries.Count > 0 &&
            !_isAiFixing &&
            !_isMassFixing;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowCloseWithoutPrompt) return;

        if (_isUpdating)
        {
            e.Cancel = true;
            StatusText.Text = "Дождитесь завершения обновления";
            return;
        }

        if (!_documents.Any(d => d.IsDirty)) return;

        var result = MessageBox.Show(this,
            "Есть несохранённые изменения. Закрыть программу без сохранения?",
            "WOJD Localization Studio",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private sealed record QualityReport(
        int Total,
        int Translated,
        int Untranslated,
        int ValidationErrors,
        int CriticalErrors,
        int ChineseText,
        int SourceConflicts,
        int GlossaryMismatches,
        int GlossaryDefinitionConflicts)
    {
        public bool HasWarnings =>
            ValidationErrors > 0 ||
            CriticalErrors > 0 ||
            ChineseText > 0 ||
            SourceConflicts > 0 ||
            GlossaryMismatches > 0 ||
            GlossaryDefinitionConflicts > 0;
    }

    private sealed record ContextEntryView(int Offset, LocalizationEntry Entry)
    {
        public string PositionText => Offset < 0 ? $"{Offset}" : $"+{Offset}";
    }

    private sealed record EntryLocation(LocalizationDocument Document, LocalizationEntry Entry);
    private sealed record TranslationEdit(
        LocalizationDocument Document,
        LocalizationEntry Entry,
        string Before,
        string After);
    private sealed record EditBatch(List<TranslationEdit> Edits, string Description);
}
