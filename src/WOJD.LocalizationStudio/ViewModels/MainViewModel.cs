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

    private readonly Dictionary<string, DocumentSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<LocalizationEntry, DocumentSession> _entrySessions =
        new();

    private readonly DispatcherTimer _searchDebounceTimer;

    private DocumentSession? _activeSession;
    private LocalizationEntry? _selectedEntry;
    private string _searchText = string.Empty;
    private string _statusFilter = "Все";
    private string? _namespaceFilter;
    private bool _isBusy;
    private string _busyText = string.Empty;

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
                () => _activeSession?.HasUnsavedChanges == true);

        SaveAllCommand =
            new RelayCommand(
                async () => await SaveAllAsync(),
                () => _sessions.Values.Any(x => x.HasUnsavedChanges));

        CloseFileCommand =
            new RelayCommand(
                async value =>
                {
                    if (value is FileNode node)
                        await CloseFileAsync(node);
                },
                value => value is FileNode { IsDirectory: false });

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
                () => _activeSession?.UndoStack.Count > 0);

        RedoCommand =
            new RelayCommand(
                RedoTranslation,
                () => _activeSession?.RedoStack.Count > 0);
    }

    public BulkObservableCollection<LocalizationEntry>
        Entries { get; } = new();

    public BulkObservableCollection<FileNode>
        FileTree { get; } = new();

    public ICollectionView EntriesView { get; }

    public string AppVersion
        => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand CloseFileCommand { get; }
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

            if (_activeSession is not null)
                _activeSession.SelectedEntry = value;

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
    public int TranslatedCount => _activeSession?.TranslatedCount ?? 0;
    public int UntranslatedCount => _activeSession?.UntranslatedCount ?? 0;
    public int ModifiedCount => _activeSession?.ModifiedCount ?? 0;
    public bool HasUnsavedChanges => _sessions.Values.Any(x => x.HasUnsavedChanges);

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

        var fullPath = Path.GetFullPath(path);

        if (_sessions.TryGetValue(fullPath, out var existing))
        {
            ActivateSession(existing);
            return;
        }

        IsBusy = true;
        BusyText =
            $"Открытие {Path.GetFileName(fullPath)}...";

        try
        {
            var document =
                await _adapter.LoadAsync(fullPath);

            var node =
                FindNodeByPath(fullPath)
                ?? new FileNode
                {
                    Name = Path.GetFileName(fullPath),
                    FullPath = fullPath,
                    IsDirectory = false,
                    EntryCount = document.Entries.Count
                };

            if (FindNodeByPath(fullPath) is null)
                FileTree.Add(node);

            node.EntryCount = document.Entries.Count;

            var session =
                new DocumentSession
                {
                    Document = document,
                    Node = node
                };

            foreach (var entry in document.Entries)
            {
                entry.PropertyChanged += Entry_PropertyChanged;
                _entrySessions[entry] = session;

                var status = entry.Status;
                session.KnownStatuses[entry] = status;
                session.KnownTranslations[entry] = entry.Translation;

                ChangeStatusCounter(
                    session,
                    status,
                    1);
            }

            _sessions[fullPath] = session;
            ActivateSession(session);
            RaiseGlobalCommandStates();
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
        var dirty =
            _sessions.Values
                .Where(x => x.HasUnsavedChanges)
                .Select(x => x.Node.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (dirty.Length == 0)
            return true;

        var preview =
            string.Join(
                Environment.NewLine,
                dirty.Take(8).Select(x => $"• {x}"));

        if (dirty.Length > 8)
            preview += $"{Environment.NewLine}• …ещё {dirty.Length - 8}";

        return AppDialog.Show(
                   $"Есть несохранённые изменения в файлах:{Environment.NewLine}{Environment.NewLine}{preview}{Environment.NewLine}{Environment.NewLine}Продолжить без сохранения?",
                   "Несохранённые изменения",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
    }

    private void ActivateSession(
        DocumentSession session)
    {
        _activeSession = session;

        Entries.ReplaceAll(
            session.Document.Entries);

        EntriesView.Refresh();

        SelectedEntry =
            session.SelectedEntry
            ?? EntriesView
                .Cast<LocalizationEntry>()
                .FirstOrDefault();

        RaiseStatsChanged();
        RaiseHistoryCommandStates();
        RaiseGlobalCommandStates();
    }

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter =
                "NDJSON/JSONL (*.ndjson;*.jsonl)|*.ndjson;*.jsonl|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        foreach (var file in dialog.FileNames)
            await LoadPathAsync(file);
    }

    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        BusyText = "Сканирование папки...";

        try
        {
            var folderPath =
                Path.GetFullPath(dialog.FolderName);

            var existingRoot =
                FileTree.FirstOrDefault(
                    x =>
                        x.IsDirectory &&
                        string.Equals(
                            Path.GetFullPath(x.FullPath),
                            folderPath,
                            StringComparison.OrdinalIgnoreCase));

            FileNode root;

            if (existingRoot is not null)
            {
                root = existingRoot;
            }
            else
            {
                root =
                    await Task.Run(
                        () => BuildTree(folderPath));

                FileTree.Add(root);
            }

            var first =
                FindFirstSupported(root);

            if (first is not null)
                await LoadPathAsync(first.FullPath);
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

    private FileNode BuildTree(
        string path)
    {
        var node =
            new FileNode
            {
                Name =
                    string.IsNullOrWhiteSpace(Path.GetFileName(path))
                        ? path
                        : Path.GetFileName(path),
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
                        FullPath = Path.GetFullPath(file),
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

            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 1024 * 1024,
                    options: FileOptions.SequentialScan);

            using var reader =
                new StreamReader(
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

    private FileNode? FindNodeByPath(
        string path)
    {
        foreach (var node in FileTree)
        {
            var found =
                FindNodeByPath(
                    node,
                    path);

            if (found is not null)
                return found;
        }

        return null;
    }

    private static FileNode? FindNodeByPath(
        FileNode node,
        string path)
    {
        if (!node.IsDirectory &&
            string.Equals(
                Path.GetFullPath(node.FullPath),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found =
                FindNodeByPath(
                    child,
                    path);

            if (found is not null)
                return found;
        }

        return null;
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
        if (_activeSession is null)
            return;

        IsBusy = true;
        BusyText = "Сохранение файла...";

        try
        {
            await SaveSessionAsync(
                _activeSession);
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

    private async Task SaveAllAsync()
    {
        var dirty =
            _sessions.Values
                .Where(x => x.HasUnsavedChanges)
                .ToArray();

        if (dirty.Length == 0)
            return;

        IsBusy = true;
        BusyText =
            $"Сохранение файлов: 0/{dirty.Length}";

        try
        {
            for (var i = 0; i < dirty.Length; i++)
            {
                BusyText =
                    $"Сохранение файлов: {i + 1}/{dirty.Length}";

                await SaveSessionAsync(
                    dirty[i]);
            }
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
            RaiseGlobalCommandStates();
        }
    }

    private async Task SaveSessionAsync(
        DocumentSession session)
    {
        BackupService.CreateBackup(
            session.Document.FilePath);

        await _adapter.SaveAsync(
            session.Document);

        RebuildStatusCache(
            session);

        session.Node.IsModified = false;

        if (ReferenceEquals(
                session,
                _activeSession))
        {
            RaiseStatsChanged();
        }

        RaiseGlobalCommandStates();
    }

    private async Task CloseFileAsync(
        FileNode node)
    {
        var path =
            Path.GetFullPath(node.FullPath);

        if (_sessions.TryGetValue(
                path,
                out var session))
        {
            if (session.HasUnsavedChanges)
            {
                var result =
                    AppDialog.Show(
                        $"В файле «{node.Name}» есть несохранённые изменения. Закрыть его без сохранения?",
                        "Закрытие файла",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            foreach (var entry in session.Document.Entries)
            {
                entry.PropertyChanged -= Entry_PropertyChanged;
                _entrySessions.Remove(entry);
            }

            _sessions.Remove(path);

            if (ReferenceEquals(
                    session,
                    _activeSession))
            {
                _activeSession = null;

                var next =
                    _sessions.Values.FirstOrDefault();

                if (next is not null)
                {
                    ActivateSession(next);
                }
                else
                {
                    Entries.ReplaceAll([]);
                    SelectedEntry = null;
                    RaiseStatsChanged();
                    RaiseHistoryCommandStates();
                }
            }
        }

        RemoveNode(
            node);

        RaiseGlobalCommandStates();

        await Task.CompletedTask;
    }

    private void RemoveNode(
        FileNode target)
    {
        if (FileTree.Remove(target))
            return;

        foreach (var root in FileTree)
        {
            if (RemoveNode(
                    root,
                    target))
            {
                return;
            }
        }
    }

    private static bool RemoveNode(
        FileNode parent,
        FileNode target)
    {
        if (parent.Children.Remove(target))
            return true;

        foreach (var child in parent.Children)
        {
            if (RemoveNode(
                    child,
                    target))
            {
                return true;
            }
        }

        return false;
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
        var session =
            _activeSession;

        if (session is null ||
            session.UndoStack.Count == 0)
        {
            return;
        }

        var edit =
            session.UndoStack.Pop();

        session.HistoryChangeInProgress = true;

        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.Translation = edit.Before;
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }

        session.RedoStack.Push(edit);
        RaiseHistoryCommandStates();
    }

    private void RedoTranslation()
    {
        var session =
            _activeSession;

        if (session is null ||
            session.RedoStack.Count == 0)
        {
            return;
        }

        var edit =
            session.RedoStack.Pop();

        session.HistoryChangeInProgress = true;

        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.Translation = edit.After;
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }

        session.UndoStack.Push(edit);
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
            sender is not LocalizationEntry entry ||
            !_entrySessions.TryGetValue(
                entry,
                out var session))
        {
            return;
        }

        var currentTranslation =
            entry.Translation;

        if (!session.KnownTranslations.TryGetValue(
                entry,
                out var previousTranslation))
        {
            previousTranslation =
                currentTranslation;
        }

        if (!session.HistoryChangeInProgress &&
            !string.Equals(
                previousTranslation,
                currentTranslation,
                StringComparison.Ordinal))
        {
            session.UndoStack.Push(
                new TranslationEdit(
                    entry,
                    previousTranslation,
                    currentTranslation));

            session.RedoStack.Clear();
        }

        session.KnownTranslations[entry] =
            currentTranslation;

        var current =
            entry.Status;

        if (!session.KnownStatuses.TryGetValue(
                entry,
                out var previous))
        {
            previous = current;
            session.KnownStatuses[entry] = current;
        }

        if (previous != current)
        {
            ChangeStatusCounter(
                session,
                previous,
                -1);

            ChangeStatusCounter(
                session,
                current,
                1);

            session.KnownStatuses[entry] =
                current;
        }

        session.Node.IsModified =
            session.HasUnsavedChanges;

        if (ReferenceEquals(
                session,
                _activeSession))
        {
            RaiseStatsChanged();
            RaiseHistoryCommandStates();

            if (StatusFilter != "Все" ||
                !string.IsNullOrWhiteSpace(SearchText))
            {
                EntriesView.Refresh();
            }
        }

        RaiseGlobalCommandStates();
    }

    private static void RebuildStatusCache(
        DocumentSession session)
    {
        session.KnownStatuses.Clear();
        session.KnownTranslations.Clear();
        session.TranslatedCount = 0;
        session.UntranslatedCount = 0;
        session.ModifiedCount = 0;

        foreach (var entry in
                 session.Document.Entries)
        {
            var status =
                entry.Status;

            session.KnownStatuses[entry] =
                status;

            session.KnownTranslations[entry] =
                entry.Translation;

            ChangeStatusCounter(
                session,
                status,
                1);
        }
    }

    private static void ChangeStatusCounter(
        DocumentSession session,
        TranslationStatus status,
        int delta)
    {
        switch (status)
        {
            case TranslationStatus.Translated:
                session.TranslatedCount += delta;
                break;

            case TranslationStatus.Untranslated:
                session.UntranslatedCount += delta;
                break;

            case TranslationStatus.Modified:
                session.ModifiedCount += delta;
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
    }

    private void RaiseGlobalCommandStates()
    {
        SaveCommand.RaiseCanExecuteChanged();
        SaveAllCommand.RaiseCanExecuteChanged();
        CloseFileCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
