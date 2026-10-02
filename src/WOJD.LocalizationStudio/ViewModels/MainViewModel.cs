using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ILocalizationFileAdapter _adapter =
        new NdjsonLocalizationAdapter();

    private readonly Dictionary<LocalizationEntry, TranslationStatus>
        _knownStatuses = new();

    private readonly Dictionary<LocalizationEntry, string>
        _knownTranslations = new();

    private readonly Stack<TranslationEdit> _undoStack = new();
    private readonly Stack<TranslationEdit> _redoStack = new();

    private readonly DispatcherTimer _searchDebounceTimer;

    private LocalizationDocument? _document;
    private LocalizationEntry? _selectedEntry;
    private string _searchText = string.Empty;
    private string _statusFilter = "Все";
    private string? _namespaceFilter;
    private bool _isBusy;
    private string _busyText = string.Empty;

    private int _translatedCount;
    private int _untranslatedCount;
    private int _modifiedCount;
    private bool _historyChangeInProgress;

    public MainViewModel()
    {
        EntriesView =
            CollectionViewSource.GetDefaultView(Entries);

        EntriesView.Filter = FilterEntry;

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };

        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            EntriesView.Refresh();
        };

        OpenFileCommand =
            new RelayCommand(
                async () => await OpenFileAsync());

        OpenFolderCommand =
            new RelayCommand(
                async () => await OpenFolderAsync());

        SaveCommand =
            new RelayCommand(
                async () => await SaveAsync(),
                () => _document is not null &&
                      HasUnsavedChanges);

        ApplyCommand =
            new RelayCommand(
                ApplyCurrent,
                () => SelectedEntry is not null);

        PreviousCommand =
            new RelayCommand(
                () => MoveSelection(-1),
                () => SelectedEntry is not null);

        NextCommand =
            new RelayCommand(
                () => MoveSelection(1),
                () => SelectedEntry is not null);

        FilterAllCommand =
            new RelayCommand(
                () => SetStatusFilter("Все", clearNamespace: true));

        FilterTranslatedCommand =
            new RelayCommand(
                () => SetStatusFilter("Переведено"));

        FilterUntranslatedCommand =
            new RelayCommand(
                () => SetStatusFilter("Без перевода"));

        FilterModifiedCommand =
            new RelayCommand(
                () => SetStatusFilter("Изменено"));

        FilterNamespaceCommand =
            new RelayCommand(
                value =>
                {
                    var ns = value as string;
                    if (string.IsNullOrWhiteSpace(ns))
                        return;

                    StatusFilter = "Все";
                    NamespaceFilter = ns;
                });

        ClearNamespaceFilterCommand =
            new RelayCommand(
                () => NamespaceFilter = null,
                () => !string.IsNullOrWhiteSpace(NamespaceFilter));

        UndoCommand =
            new RelayCommand(
                UndoTranslation,
                () => _undoStack.Count > 0);

        RedoCommand =
            new RelayCommand(
                RedoTranslation,
                () => _redoStack.Count > 0);
    }

    public BulkObservableCollection<LocalizationEntry>
        Entries { get; } = new();

    public BulkObservableCollection<FileNode>
        FileTree { get; } = new();

    public ICollectionView EntriesView { get; }

    public string[] StatusFilters { get; } =
        ["Все", "Без перевода", "Переведено", "Изменено"];

    public string AppVersion
        => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand FilterAllCommand { get; }
    public RelayCommand FilterTranslatedCommand { get; }
    public RelayCommand FilterUntranslatedCommand { get; }
    public RelayCommand FilterModifiedCommand { get; }
    public RelayCommand FilterNamespaceCommand { get; }
    public RelayCommand ClearNamespaceFilterCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }

    public LocalizationEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;

            PreviousCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
                return;

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
                EntriesView.Refresh();
        }
    }

    public string? NamespaceFilter
    {
        get => _namespaceFilter;
        private set
        {
            if (SetProperty(ref _namespaceFilter, value))
            {
                OnPropertyChanged(nameof(NamespaceFilterLabel));
                EntriesView.Refresh();
                ClearNamespaceFilterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NamespaceFilterLabel
        => string.IsNullOrWhiteSpace(NamespaceFilter)
            ? string.Empty
            : $"Namespace: {NamespaceFilter}";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public int TotalCount => Entries.Count;
    public int TranslatedCount => _translatedCount;
    public int UntranslatedCount => _untranslatedCount;
    public int ModifiedCount => _modifiedCount;
    public bool HasUnsavedChanges => _modifiedCount > 0;

    public async Task LoadPathAsync(
        string path,
        bool confirmDiscard = true)
    {
        if (!_adapter.CanOpen(path))
        {
            AppDialog.Show(
                "Пока подключён базовый адаптер NDJSON/JSONL.",
                "Формат файла",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (confirmDiscard &&
            !ConfirmDiscardUnsaved())
        {
            return;
        }

        IsBusy = true;
        BusyText =
            $"Открытие {Path.GetFileName(path)}...";

        try
        {
            // Адаптер выполняет чтение и JSON-разбор вне UI-потока.
            var document =
                await _adapter.LoadAsync(path);

            UnsubscribeEntries();

            _document = document;

            _knownStatuses.Clear();
            _knownTranslations.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _historyChangeInProgress = false;
            _translatedCount = 0;
            _untranslatedCount = 0;
            _modifiedCount = 0;

            foreach (var entry in document.Entries)
            {
                entry.PropertyChanged +=
                    Entry_PropertyChanged;

                var status = entry.Status;
                _knownStatuses[entry] = status;
                _knownTranslations[entry] = entry.Translation;
                ChangeStatusCounter(status, 1);
            }

            // Одно Reset-событие вместо десятков тысяч Add.
            Entries.ReplaceAll(document.Entries);

            RaiseStatsChanged();
            RaiseHistoryCommandStates();

            SelectedEntry =
                EntriesView
                    .Cast<LocalizationEntry>()
                    .FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка открытия",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BusyText = string.Empty;
            IsBusy = false;
        }
    }

    public bool ConfirmDiscardUnsaved()
    {
        if (!HasUnsavedChanges)
            return true;

        return AppDialog.Show(
                   "Есть несохранённые изменения. Продолжить без сохранения?",
                   "Несохранённые изменения",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
    }

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter =
                "NDJSON/JSONL (*.ndjson;*.jsonl)|*.ndjson;*.jsonl|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        await LoadPathAsync(dialog.FileName);

        if (_document is not null &&
            string.Equals(
                _document.FilePath,
                dialog.FileName,
                StringComparison.OrdinalIgnoreCase))
        {
            var alreadyOpen =
                FileTree.Any(
                    node =>
                        !node.IsDirectory &&
                        string.Equals(
                            node.FullPath,
                            dialog.FileName,
                            StringComparison.OrdinalIgnoreCase));

            if (!alreadyOpen)
            {
                FileTree.Add(
                    new FileNode
                    {
                        Name =
                            Path.GetFileName(dialog.FileName),
                        FullPath = dialog.FileName,
                        IsDirectory = false,
                        EntryCount = Entries.Count
                    });
            }
        }
    }

    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() != true)
            return;

        if (!ConfirmDiscardUnsaved())
            return;

        IsBusy = true;
        BusyText = "Сканирование папки...";

        try
        {
            // Подсчёт строк по файлам больше не блокирует окно.
            var root =
                await Task.Run(
                    () => BuildTree(dialog.FolderName));

            FileTree.ReplaceAll([root]);

            var first = FindFirstSupported(root);

            if (first is not null)
            {
                await LoadPathAsync(
                    first.FullPath,
                    confirmDiscard: false);
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка открытия папки",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BusyText = string.Empty;
            IsBusy = false;
        }
    }

    private FileNode BuildTree(string path)
    {
        var node = new FileNode
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = true
        };

        try
        {
            foreach (var dir in
                     Directory
                         .EnumerateDirectories(path)
                         .OrderBy(x => x))
            {
                node.Children.Add(
                    BuildTree(dir));
            }

            foreach (var file in
                     Directory
                         .EnumerateFiles(path)
                         .Where(_adapter.CanOpen)
                         .OrderBy(x => x))
            {
                node.Children.Add(
                    new FileNode
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        IsDirectory = false,
                        EntryCount = CountFileRows(file)
                    });
            }
        }
        catch
        {
            // Недоступные подпапки пропускаются.
        }

        return node;
    }

    private static int CountFileRows(
        string path)
    {
        try
        {
            var count = 0;

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 1024 * 1024,
                options: FileOptions.SequentialScan);

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024 * 1024);

            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static FileNode? FindFirstSupported(
        FileNode node)
    {
        if (!node.IsDirectory)
            return node;

        foreach (var child in node.Children)
        {
            var found =
                FindFirstSupported(child);

            if (found is not null)
                return found;
        }

        return null;
    }

    private async Task SaveAsync()
    {
        if (_document is null)
            return;

        IsBusy = true;
        BusyText = "Сохранение файла...";

        try
        {
            BackupService.CreateBackup(
                _document.FilePath);

            await _adapter.SaveAsync(
                _document);

            RebuildStatusCache();
            RaiseStatsChanged();
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка сохранения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BusyText = string.Empty;
            IsBusy = false;
        }
    }

    private void ApplyCurrent()
    {
        if (StatusFilter != "Все" ||
            !string.IsNullOrWhiteSpace(SearchText))
        {
            EntriesView.Refresh();
        }

        MoveSelection(1);
    }

    private void MoveSelection(
        int delta)
    {
        if (SelectedEntry is null)
            return;

        EntriesView.MoveCurrentTo(
            SelectedEntry);

        var moved =
            delta < 0
                ? EntriesView.MoveCurrentToPrevious()
                : EntriesView.MoveCurrentToNext();

        if (moved &&
            EntriesView.CurrentItem is LocalizationEntry entry)
        {
            SelectedEntry = entry;
        }
    }

    private bool FilterEntry(
        object obj)
    {
        if (obj is not LocalizationEntry entry)
            return false;

        if (StatusFilter != "Все" &&
            entry.StatusText != StatusFilter)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(NamespaceFilter) &&
            !string.Equals(
                entry.Namespace,
                NamespaceFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim();

        return entry.Namespace.Contains(
                   q,
                   StringComparison.OrdinalIgnoreCase)
               || entry.Key.Contains(
                   q,
                   StringComparison.OrdinalIgnoreCase)
               || entry.Original.Contains(
                   q,
                   StringComparison.OrdinalIgnoreCase)
               || entry.Translation.Contains(
                   q,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatusFilter(
        string status,
        bool clearNamespace = false)
    {
        if (clearNamespace)
            NamespaceFilter = null;

        StatusFilter = status;
    }

    private void UndoTranslation()
    {
        if (_undoStack.Count == 0)
            return;

        var edit = _undoStack.Pop();

        _historyChangeInProgress = true;
        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.Translation = edit.Before;
        }
        finally
        {
            _historyChangeInProgress = false;
        }

        _redoStack.Push(edit);
        RaiseHistoryCommandStates();
    }

    private void RedoTranslation()
    {
        if (_redoStack.Count == 0)
            return;

        var edit = _redoStack.Pop();

        _historyChangeInProgress = true;
        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.Translation = edit.After;
        }
        finally
        {
            _historyChangeInProgress = false;
        }

        _undoStack.Push(edit);
        RaiseHistoryCommandStates();
    }

    private void RaiseHistoryCommandStates()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private void Entry_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName !=
                nameof(LocalizationEntry.Translation) ||
            sender is not LocalizationEntry entry)
        {
            return;
        }

        var currentTranslation = entry.Translation;

        if (!_knownTranslations.TryGetValue(
                entry,
                out var previousTranslation))
        {
            previousTranslation = currentTranslation;
        }

        if (!_historyChangeInProgress &&
            !string.Equals(
                previousTranslation,
                currentTranslation,
                StringComparison.Ordinal))
        {
            _undoStack.Push(
                new TranslationEdit(
                    entry,
                    previousTranslation,
                    currentTranslation));

            _redoStack.Clear();
            RaiseHistoryCommandStates();
        }

        _knownTranslations[entry] = currentTranslation;

        var current = entry.Status;

        if (!_knownStatuses.TryGetValue(
                entry,
                out var previous))
        {
            previous = current;
            _knownStatuses[entry] = current;
        }

        if (previous != current)
        {
            ChangeStatusCounter(
                previous,
                -1);

            ChangeStatusCounter(
                current,
                1);

            _knownStatuses[entry] = current;
            RaiseStatsChanged();
        }

        if (StatusFilter != "Все" ||
            !string.IsNullOrWhiteSpace(SearchText))
        {
            EntriesView.Refresh();
        }

        SaveCommand.RaiseCanExecuteChanged();
    }

    private void RebuildStatusCache()
    {
        _knownStatuses.Clear();
        _translatedCount = 0;
        _untranslatedCount = 0;
        _modifiedCount = 0;

        foreach (var entry in Entries)
        {
            var status = entry.Status;
            _knownStatuses[entry] = status;
            ChangeStatusCounter(status, 1);
        }
    }

    private void ChangeStatusCounter(
        TranslationStatus status,
        int delta)
    {
        switch (status)
        {
            case TranslationStatus.Translated:
                _translatedCount += delta;
                break;

            case TranslationStatus.Untranslated:
                _untranslatedCount += delta;
                break;

            case TranslationStatus.Modified:
                _modifiedCount += delta;
                break;
        }
    }

    private void RaiseStatsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(UntranslatedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));

        SaveCommand.RaiseCanExecuteChanged();
    }

    private void UnsubscribeEntries()
    {
        foreach (var entry in Entries)
        {
            entry.PropertyChanged -=
                Entry_PropertyChanged;
        }
    }

    private sealed record TranslationEdit(
        LocalizationEntry Entry,
        string Before,
        string After);
}
