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
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly ObservableCollection<LocalizationDocument> _documents = new();
    private ICollectionView? _view;
    private LocalizationDocument? _currentDocument;
    private LocalizationEntry? _selected;
    private bool _suppressEditor;
    private bool _isCheckingForUpdates;
    private bool _allowCloseWithoutPrompt;
    private string? _lastNotifiedUpdateVersion;

    public MainWindow()
    {
        InitializeComponent();
        FilesList.ItemsSource = _documents;

        Loaded += MainWindow_Loaded;
        _updateTimer.Tick += UpdateTimer_Tick;

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, async (_, _) => await OpenFilesAsync()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, async (_, _) => await SaveCurrentAsync()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _updateTimer.Start();
        await Task.Delay(1200);
        await CheckForUpdatesAsync();
    }

    private async void UpdateTimer_Tick(object? sender, EventArgs e) => await CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates) return;

        _isCheckingForUpdates = true;
        try
        {
            var update = await _updateService.CheckAsync();
            if (update is null || update.Version == _lastNotifiedUpdateVersion)
                return;

            _lastNotifiedUpdateVersion = update.Version;

            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? string.Empty
                : $"\n\nЧто нового:\n{update.Notes}";

            var result = MessageBox.Show(
                this,
                $"Доступна новая версия WOJD Localization Studio v{update.Version}.\n" +
                $"Текущая версия: v{_updateService.CurrentVersion}.{notes}\n\nОбновить сейчас?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                await BeginInAppUpdateAsync();
        }
        catch
        {
            // Отсутствие интернета или временная ошибка GitHub не мешает работе редактора.
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private async Task BeginInAppUpdateAsync()
    {
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

        if (!_updateService.TryLaunchUpdater(out var error))
        {
            MessageBox.Show(
                this,
                error ?? "Не удалось запустить обновление.",
                "Ошибка обновления",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _allowCloseWithoutPrompt = true;
        _updateTimer.Stop();
        Application.Current.Shutdown();
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

        LocalizationDocument? lastOpened = null;

        foreach (var path in dialog.FileNames)
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
                var loaded = await _service.LoadAsync(path);
                ValidationService.ValidateAll(loaded);

                var document = new LocalizationDocument { FilePath = path };
                foreach (var item in loaded)
                    document.Entries.Add(item);

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

        if (lastOpened is not null)
            FilesList.SelectedItem = lastOpened;

        UpdateButtons();
        StatusText.Text = _documents.Count == 0
            ? "Файлы не открыты"
            : $"Открыто файлов: {_documents.Count}";
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
        _view.Filter = FilterEntry;
        _view.Refresh();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not LocalizationEntry entry) return false;

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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private void SearchScopeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

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
        UpdateValidationPanel();

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
        ValidationStatusText.Text = "Выберите строку";
        ValidationStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x79, 0x8A));
        _suppressEditor = false;
    }

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditor || _selected is null || _currentDocument is null) return;

        _selected.Translation = TranslationBox.Text;
        ValidationService.Validate(_selected);
        _currentDocument.IsDirty = true;
        _currentDocument.RefreshComputedProperties();

        LengthLabel.Text = $"{TranslationBox.Text.Length:N0} символов";
        UpdateValidationPanel();
        UpdateCounters();
        _view?.Refresh();
        StatusText.Text = $"Есть несохранённые изменения: {_currentDocument.FileName}";
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
            return;
        }

        var total = _currentDocument.Entries.Count;
        var translated = _currentDocument.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Translation));
        var untranslated = total - translated;
        var errors = _currentDocument.Entries.Count(e => e.HasValidationIssues);

        TotalBadge.Text = $"Все  {total:N0}";
        TranslatedBadge.Text = $"Переведено  {translated:N0}";
        UntranslatedBadge.Text = $"Не переведено  {untranslated:N0}";
        ErrorBadge.Text = $"Ошибки  {errors:N0}";
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
        if (!_documents.Any(d => d.IsDirty)) return;

        var result = MessageBox.Show(this,
            "Есть несохранённые изменения. Закрыть программу без сохранения?",
            "WOJD Localization Studio",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }
}
