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
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ObservableCollection<LocalizationDocument> _documents = new();
    private ICollectionView? _view;
    private LocalizationDocument? _currentDocument;
    private LocalizationEntry? _selected;
    private bool _suppressEditor;
    private bool _isCheckingForUpdates;
    private bool _isUpdating;
    private bool _allowCloseWithoutPrompt;
    private UpdateInfo? _availableUpdate;
    private string _statusFilter = "All";
    private string? _namespaceFilter;
    private readonly Dictionary<string, List<EntryLocation>> _sourceIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _conflictingSources = new(StringComparer.Ordinal);
    private readonly Stack<EditBatch> _undoStack = new();
    private readonly Stack<EditBatch> _redoStack = new();

    public MainWindow()
    {
        InitializeComponent();
        FilesList.ItemsSource = _documents;
        UpdateFilterVisuals();
        UpdateBottomSummary();

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
            !string.Equals(_statusFilter, "All", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(SearchBox.Text);

        Predicate<object>? desiredFilter = hasFilter ? FilterEntry : null;

        if (!Equals(_view.Filter, desiredFilter))
            _view.Filter = desiredFilter;
        else if (hasFilter)
            _view.Refresh();

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
            "Modified" => entry.IsModified,
            _ => true
        };

        if (!matchesStatus) return false;

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

    private void FilterBadge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filter)
            return;

        _statusFilter = filter;

        if (string.Equals(filter, "All", StringComparison.Ordinal))
            _namespaceFilter = null;

        UpdateFilterVisuals();
        UpdateBottomSummary();
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
        SearchBox.Clear();

        UpdateFilterVisuals();
        UpdateBottomSummary();
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
        RefreshConflictState(_selected.Source);
        _currentDocument.IsDirty = true;
        _currentDocument.RefreshComputedProperties();

        LengthLabel.Text = $"{after.Length:N0} символов";
        UpdateExactMatchesPanel();
        UpdateValidationPanel();
        UpdateCounters();
        _view?.Refresh();
        StatusText.Text = $"Есть несохранённые изменения: {_currentDocument.FileName}";
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
        var edits = new List<TranslationEdit>();

        foreach (var item in matches)
        {
            if (string.Equals(item.Entry.Translation, translation, StringComparison.Ordinal))
                continue;

            edits.Add(new TranslationEdit(
                item.Document,
                item.Entry,
                item.Entry.Translation,
                translation));
        }

        if (edits.Count == 0)
        {
            StatusText.Text = "Все точные совпадения уже имеют этот перевод";
            UpdateExactMatchesPanel();
            return;
        }

        var batch = new EditBatch(edits, "Применение к точным совпадениям");
        RecordEditBatch(batch);
        ApplyEditBatch(batch, useAfter: true);

        var affectedFiles = edits
            .Select(edit => edit.Document.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        StatusText.Text =
            $"Перевод применён к {edits.Count:N0} строкам с 100% совпадением в {affectedFiles:N0} файлах";
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

            UpdateExactMatchesPanel();
            UpdateValidationPanel();
        }

        EntriesGrid.Items.Refresh();
        _view?.Refresh();
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
        if (_selected is null)
        {
            ValidationStatusText.Text = "Выберите строку";
            return;
        }

        if (string.IsNullOrWhiteSpace(_selected.Translation))
        {
            ValidationStatusText.Text = "— Строка не переведена. Проверка служебных элементов будет выполнена после ввода перевода.";
            ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x79, 0x8A));
            return;
        }

        if (_selected.HasValidationIssues)
        {
            ValidationStatusText.Text = $"⚠ {_selected.ValidationSummary}";
            ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB3, 0x3A, 0x2B));
            return;
        }

        var tokens = ValidationService.ExtractTokens(_selected.Source);
        ValidationStatusText.Text = tokens.Count == 0
            ? "✓ Служебные элементы не найдены."
            : $"✓ Все служебные элементы сохранены: {string.Join(", ", tokens)}";
        ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x75, 0x40));
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
            ModifiedBadge.Text = "Изменённые  0";
            FileSummaryText.Text = "Файлы не открыты";
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
        ConflictBadge.Text = $"Конфликты  {conflicts:N0}";
        ModifiedBadge.Text = $"Изменённые  {modified:N0}";
        UpdateBottomSummary(total, translated);
    }

    private void UpdateBottomSummary()
    {
        if (_currentDocument is null)
        {
            FileSummaryText.Text = "Файлы не открыты";
            return;
        }

        var total = _currentDocument.Entries.Count;
        var translated = _currentDocument.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Translation));
        UpdateBottomSummary(total, translated);
    }

    private void UpdateBottomSummary(int total, int translated)
    {
        if (_currentDocument is null)
        {
            FileSummaryText.Text = "Файлы не открыты";
            return;
        }

        var untranslated = total - translated;
        var namespaceSuffix = _namespaceFilter is null
            ? string.Empty
            : $"  •  Namespace: {_namespaceFilter}";

        FileSummaryText.Text =
            $"{_currentDocument.FileName}  •  {total:N0} строк  •  {translated:N0} переведено  •  {untranslated:N0} пустых{namespaceSuffix}";
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null) return;

        StatusText.Text = $"Проверка: {_currentDocument.FileName}…";
        ValidationService.ValidateAll(_currentDocument.Entries);
        _currentDocument.RefreshComputedProperties();
        EntriesGrid.Items.Refresh();
        _view?.Refresh();
        UpdateCounters();

        var firstError = _currentDocument.Entries.FirstOrDefault(e => e.HasValidationIssues);
        if (firstError is null)
        {
            StatusText.Text = $"Проверка завершена: ошибок нет — {_currentDocument.FileName}";
        }
        else
        {
            EntriesGrid.SelectedItem = firstError;
            EntriesGrid.ScrollIntoView(firstError);
            StatusText.Text = $"Проверка завершена: {_currentDocument.ValidationErrorCount:N0} ошибок — {_currentDocument.FileName}";
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

    private async Task<bool> SaveDocumentAsync(LocalizationDocument document, bool showStatus = true)
    {
        try
        {
            if (showStatus) StatusText.Text = $"Сохранение: {document.FileName}…";
            await _service.SaveAsync(document.FilePath, document.Entries);

            foreach (var entry in document.Entries)
                entry.IsModified = false;

            document.IsDirty = false;
            document.RefreshComputedProperties();
            EntriesGrid.Items.Refresh();
            _view?.Refresh();

            if (ReferenceEquals(document, _currentDocument))
                UpdateCounters();

            if (showStatus) StatusText.Text = $"Сохранено: {document.FileName}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Не удалось сохранить {document.FileName}.\n\n{ex.Message}",
                "Ошибка сохранения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = $"Ошибка сохранения: {document.FileName}";
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

    private sealed record EntryLocation(LocalizationDocument Document, LocalizationEntry Entry);
    private sealed record TranslationEdit(
        LocalizationDocument Document,
        LocalizationEntry Entry,
        string Before,
        string After);
    private sealed record EditBatch(List<TranslationEdit> Edits, string Description);
}
