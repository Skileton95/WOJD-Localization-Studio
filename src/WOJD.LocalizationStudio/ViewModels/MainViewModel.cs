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

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILocalizationFileAdapter _adapter =
        new NdjsonLocalizationAdapter();

    private readonly Dictionary<string, DocumentSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<LocalizationEntry, DocumentSession> _entrySessions =
        new();

    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly DispatcherTimer _workspaceSaveTimer;

    private readonly Dictionary<string, DraftFileState> _recoveryDrafts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _projectSearchHistory = [];

    private DocumentSession? _activeSession;
    private LocalizationEntry? _selectedEntry;
    private int _lastSelectedIndex;
    private string _searchText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _isReplacePanelVisible;
    private string _statusFilter = "Все";
    private string? _namespaceFilter;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private bool _workspaceRestoreInProgress;
    private int _restoredDraftEntries;

    public MainViewModel()
    {
        RestoreEditHistory();
        EntriesView =
            CollectionViewSource.GetDefaultView(Entries);

        EntriesView.Filter = FilterEntry;

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Settings.SearchDebounceMs)
        };

        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RefreshNamespaces();
            RefreshConsistency();
            EntriesView.Refresh();
        };

        _workspaceSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };

        _workspaceSaveTimer.Tick += (_, _) =>
        {
            _workspaceSaveTimer.Stop();

            if (!_workspaceRestoreInProgress)
                PersistWorkspaceState(includeDrafts: true);
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
                () => !IsBusy && _activeSession?.HasUnsavedChanges == true);

        SaveAllCommand =
            new RelayCommand(
                async () => await SaveAllAsync(),
                () => !IsBusy && _sessions.Values.Any(x => x.HasUnsavedChanges));

        CloseFileCommand =
            new RelayCommand(
                async value =>
                {
                    if (value is FileNode node)
                        await CloseFileAsync(node);
                },
                value => !IsBusy && value is FileNode { IsDirectory: false });

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

        FilterErrorsCommand =
            new RelayCommand(
                () => SetStatusFilter("Ошибки"));

        FilterNamespaceCommand =
            new RelayCommand(
                value =>
                {
                    var ns = value as string;
                    if (ns is null)
                        return;

                    StatusFilter = "Все";
                    NamespaceFilter = ns;
                });

        ClearNamespaceFilterCommand =
            new RelayCommand(
                () => NamespaceFilter = null,
                () => NamespaceFilter is not null);

        UndoCommand =
            new RelayCommand(
                UndoTranslation,
                () => _activeSession?.UndoStack.Count > 0);

        RedoCommand =
            new RelayCommand(
                RedoTranslation,
                () => _activeSession?.RedoStack.Count > 0);

        ToggleReplaceCommand =
            new RelayCommand(
                () => IsReplacePanelVisible = !IsReplacePanelVisible);

        ReplaceCurrentCommand =
            new RelayCommand(
                ReplaceCurrent,
                () => SelectedEntry is not null &&
                      !string.IsNullOrEmpty(SearchText));

        ReplaceAllCommand =
            new RelayCommand(
                ReplaceAll,
                () => _activeSession is not null &&
                      !string.IsNullOrEmpty(SearchText));

        NextUntranslatedCommand =
            new RelayCommand(
                () => NavigateUntranslated(1),
                () => _activeSession is not null);

        PreviousUntranslatedCommand =
            new RelayCommand(
                () => NavigateUntranslated(-1),
                () => _activeSession is not null);
    }

    public BulkObservableCollection<LocalizationEntry>
        Entries { get; } = new();

    public BulkObservableCollection<FileNode>
        FileTree { get; } = new();

    public ICollectionView EntriesView { get; }

    public BulkObservableCollection<FileNode> OpenTabs { get; } = new();
    public WindowLayoutState? WindowLayout { get; set; }

    public void CycleTab(int direction)
    {
        if (OpenTabs.Count == 0 || IsBusy) return;
        var index = _activeSession is null ? 0 : OpenTabs.IndexOf(_activeSession.Node);
        var node = OpenTabs[(index + direction + OpenTabs.Count) % OpenTabs.Count];
        ActivateSession(_sessions[Path.GetFullPath(node.FullPath)]);
    }

    public void TogglePin(FileNode node)
    {
        node.IsPinned = !node.IsPinned;
        ScheduleWorkspaceSave();
    }

    public Task CloseCurrentFileAsync() => _activeSession is null || IsBusy
        ? Task.CompletedTask : CloseFileAsync(_activeSession.Node);

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
    public RelayCommand FilterErrorsCommand { get; }
    public RelayCommand FilterNamespaceCommand { get; }
    public RelayCommand ClearNamespaceFilterCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand ToggleReplaceCommand { get; }
    public RelayCommand ReplaceCurrentCommand { get; }
    public RelayCommand ReplaceAllCommand { get; }
    public RelayCommand NextUntranslatedCommand { get; }
    public RelayCommand PreviousUntranslatedCommand { get; }

    public LocalizationEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;
            OnPropertyChanged(nameof(GlossaryHints));
            OnPropertyChanged(nameof(SelectedContextText));

            if (value is not null) _lastSelectedIndex = value.Index;
            if (_activeSession is not null)
                _activeSession.SelectedEntry = value;

            PreviousCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            ReplaceCurrentCommand.RaiseCanExecuteChanged();
            ScheduleWorkspaceSave();
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
            ReplaceCurrentCommand.RaiseCanExecuteChanged();
            ReplaceAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value);
    }

    public bool IsReplacePanelVisible
    {
        get => _isReplacePanelVisible;
        set => SetProperty(ref _isReplacePanelVisible, value);
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

    public bool IsNotBusy => !IsBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsNotBusy));
            RaiseGlobalCommandStates();
            if (value) { _operationCancellation = new(); BusyPercent = 0; }
            else { _operationCancellation?.Dispose(); _operationCancellation = null; }
        }
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
    public int ErrorCount => _activeSession?.ValidationErrorCount ?? 0;
    public LocalizationDocument? ActiveDocument => _activeSession?.Document;
    public IReadOnlyList<string> ProjectSearchHistory => _projectSearchHistory;
    public bool HasUnsavedChanges => _sessions.Values.Any(x => x.HasUnsavedChanges);

    public IReadOnlyList<LocalizationEntry> GetUntranslatedEntries()
        => _activeSession?.Document.Entries
               .Where(x => x.Status == TranslationStatus.Untranslated)
               .ToList()
           ?? [];

    public IReadOnlyList<LocalizationEntry> GetErrorEntries()
        => _activeSession?.Document.Entries
               .Where(x => x.HasValidationIssues)
               .ToList()
           ?? [];

    public void CopyOriginalToTranslation(
        IEnumerable<LocalizationEntry> entries)
    {
        foreach (var entry in entries.Distinct())
            entry.Translation = entry.Original;
    }

    public void ClearTranslations(
        IEnumerable<LocalizationEntry> entries)
    {
        foreach (var entry in entries.Distinct())
            entry.Translation = string.Empty;
    }

    public async Task ExportEntriesAsync(
        IEnumerable<LocalizationEntry> entries,
        string suffix)
    {
        var items =
            entries.Distinct().ToList();

        if (items.Count == 0)
        {
            AppDialog.Show(
                "Нет строк для экспорта.",
                "Экспорт",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var baseName =
            _activeSession is null
                ? "export"
                : Path.GetFileNameWithoutExtension(
                    _activeSession.Document.FilePath);

        var dialog =
            new SaveFileDialog
            {
                Filter = "NDJSON (*.ndjson)|*.ndjson|JSONL (*.jsonl)|*.jsonl",
                FileName = $"{baseName}.{suffix}.ndjson",
                AddExtension = true,
                DefaultExt = ".ndjson"
            };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        BusyText = $"Экспорт строк: {items.Count}";

        try
        {
            await NdjsonExportService.ExportAsync(
                items,
                dialog.FileName);

            AppDialog.Show(
                $"Экспортировано строк: {items.Count}.",
                "Экспорт завершён",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка экспорта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BusyText = string.Empty;
            IsBusy = false;
        }
    }

    public async Task<ProjectSearchResponse> SearchProjectAsync(
        string query,
        bool matchCase,
        bool exactMatch,
        bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ProjectSearchResponse([], false);

        RememberProjectSearchQuery(query);

        var documents =
            _sessions.Values
                .Select(x => x.Document)
                .ToList();

        return await Task.Run(
            () =>
                ProjectSearchService.Search(
                    documents,
                    query.Trim(),
                    matchCase,
                    exactMatch,
                    useRegex));
    }

    public void OpenProjectSearchResult(
        ProjectSearchResult result)
    {
        var path =
            Path.GetFullPath(result.FilePath);

        if (!_sessions.TryGetValue(
                path,
                out var session))
        {
            return;
        }

        ActivateSession(session);

        StatusFilter = "Все";
        NamespaceFilter = null;
        SearchText = string.Empty;
        EntriesView.Refresh();

        ResetAllFilters();
        SelectedEntry = result.Entry;
        EntriesView.MoveCurrentTo(result.Entry);
    }

    private void RememberProjectSearchQuery(
        string query)
    {
        query = query.Trim();

        if (query.Length == 0)
            return;

        _projectSearchHistory.RemoveAll(
            x =>
                string.Equals(
                    x,
                    query,
                    StringComparison.OrdinalIgnoreCase));

        _projectSearchHistory.Insert(0, query);

        if (_projectSearchHistory.Count > 20)
        {
            _projectSearchHistory.RemoveRange(
                20,
                _projectSearchHistory.Count - 20);
        }

        ScheduleWorkspaceSave();
    }

    public async Task RestoreWorkspaceAsync()
    {
        var state =
            WorkspaceStateService.Load();

        if (state is null)
            return;

        WindowLayout = state.Layout;
        _namespaceFavorites = (state.NamespaceFavorites ?? []).Select(x => (x.FilePath, x.Namespace)).ToHashSet();
        NamespacePanelExpanded = state.NamespacePanelExpanded;
        _blockedRecovery.Clear(); _blockedRecovery.AddRange(state.BlockedRecovery ?? []);
        NotesOnly = state.NotesOnly;
        _consistencyExceptions = (state.ConsistencyExceptions ?? []).ToHashSet(StringComparer.Ordinal);
        SavedFilters = state.SavedFilters ?? []; SetSmartFilters(state.SmartFilters ?? new());

        _workspaceRestoreInProgress = true;
        _restoredDraftEntries = 0;
        _recoveryDrafts.Clear();
        _projectSearchHistory.Clear();

        if (state.SearchHistory is not null)
        {
            _projectSearchHistory.AddRange(
                state.SearchHistory
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20));
        }

        foreach (var draft in state.Drafts)
        {
            if (string.IsNullOrWhiteSpace(draft.FilePath))
                continue;

            _recoveryDrafts[Path.GetFullPath(draft.FilePath)] =
                draft;
        }

        try
        {
            foreach (var folder in state.OpenFolders)
            {
                if (!Directory.Exists(folder))
                    continue;

                var fullFolder =
                    Path.GetFullPath(folder);

                var exists =
                    FileTree.Any(
                        x =>
                            x.IsDirectory &&
                            string.Equals(
                                Path.GetFullPath(x.FullPath),
                                fullFolder,
                                StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    var root =
                        await Task.Run(
                            () => BuildTree(fullFolder));

                    FileTree.Add(root);
                }
            }

            foreach (var file in state.OpenFiles)
            {
                if (File.Exists(file))
                    await LoadPathAsync(file);
            }

            foreach (var node in OpenTabs)
                node.IsPinned = state.PinnedFiles?.Contains(node.FullPath, StringComparer.OrdinalIgnoreCase) == true;

            if (state.ExpandedNamespaceFiles is not null)
                foreach (var group in NamespaceTree) group.IsExpanded = state.ExpandedNamespaceFiles.Contains(group.FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in state.SelectedRows)
            {
                var path =
                    Path.GetFullPath(pair.Key);

                if (!_sessions.TryGetValue(path, out var session))
                    continue;

                session.SelectedEntry =
                    session.Document.Entries
                        .FirstOrDefault(x => x.Index == pair.Value)
                    ?? session.SelectedEntry;
            }

            if (!string.IsNullOrWhiteSpace(state.ActiveFile))
            {
                var activePath =
                    Path.GetFullPath(state.ActiveFile);

                if (_sessions.TryGetValue(
                        activePath,
                        out var activeSession))
                {
                    ActivateSession(activeSession);
                }
            }
        }
        finally
        {
            _workspaceRestoreInProgress = false;
            _recoveryDrafts.Clear();
        }

        if (_restoredDraftEntries > 0)
        {
            AppDialog.Show(
                $"Восстановлено несохранённых переводов: {_restoredDraftEntries}.",
                "Восстановление черновика",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        ScheduleWorkspaceSave();
    }

    public void PersistWorkspaceState(
        bool includeDrafts)
    {
        try
        {
            var openFiles =
                OpenTabs.Select(x => x.FullPath)
                    .ToList();

            var openFolders =
                FileTree
                    .Where(x => x.IsDirectory)
                    .Select(x => Path.GetFullPath(x.FullPath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var selectedRows =
                _sessions.Values
                    .Where(x => x.SelectedEntry is not null)
                    .ToDictionary(
                        x => Path.GetFullPath(x.Document.FilePath),
                        x => x.SelectedEntry!.Index,
                        StringComparer.OrdinalIgnoreCase);

            var drafts =
                new List<DraftFileState>();

            if (includeDrafts)
            {
                foreach (var session in _sessions.Values)
                {
                    var entries =
                        session.Document.Entries
                            .Where(x => x.Status == TranslationStatus.Modified)
                            .Select(
                                x =>
                                    new DraftEntryState(
                                        x.Index,
                                        x.SavedNamespace,
                                        x.SavedKey,
                                        x.Translation,
                                        x.NamespaceModified ? x.Namespace : null,
                                        x.KeyModified ? x.Key : null,
                                        x.OriginalModified ? x.Original : null,
                                        x.SavedOriginal))
                            .ToList();

                    if (entries.Count > 0)
                    {
                        drafts.Add(
                            new DraftFileState(
                                Path.GetFullPath(session.Document.FilePath),
                                entries));
                    }
                }
            }

            var state =
                new WorkspaceState(
                    openFiles,
                    openFolders,
                    _activeSession is null
                        ? null
                        : Path.GetFullPath(_activeSession.Document.FilePath),
                    selectedRows,
                    drafts,
                    _projectSearchHistory.ToList(),
                    OpenTabs.Where(x => x.IsPinned).Select(x => x.FullPath).ToList(),
                    WindowLayout,
                    _namespaceFavorites.Select(x => new NamespaceBookmark(x.FilePath, x.Namespace)).ToList(),
                    NamespaceTree.Where(x => x.IsExpanded).Select(x => x.FilePath).ToList(), NamespacePanelExpanded, SavedFilters.ToList(), SmartFilters, _consistencyExceptions.ToList(), NotesOnly, _blockedRecovery.ToList());

            WorkspaceStateService.Save(state);
        }
        catch
        {
            // Session persistence must never block editing.
        }
    }

    private void ScheduleWorkspaceSave()
    {
        if (_workspaceRestoreInProgress)
            return;

        _workspaceSaveTimer.Stop();
        _workspaceSaveTimer.Start();
    }

    public async Task LoadPathAsync(
        string path,
        bool confirmDiscard = true)
    {
        if (IsBusy) return;
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
        DocumentSession? pendingSession = null;

        try
        {
            var document =
                await _adapter.LoadAsync(fullPath, _operationCancellation!.Token, OperationProgress());

            if (_recoveryDrafts.TryGetValue(
                    fullPath,
                    out var recoveryDraft))
            {
                foreach (var draftEntry in recoveryDraft.Entries)
                {
                    var entry =
                        document.Entries.FirstOrDefault(
                            x =>
                                x.Index == draftEntry.Index &&
                                string.Equals(
                                    x.Namespace,
                                    draftEntry.Namespace,
                                    StringComparison.Ordinal) &&
                                string.Equals(
                                    x.Key,
                                    draftEntry.Key,
                                    StringComparison.Ordinal))
                        ?? document.Entries.FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.Namespace,
                                    draftEntry.Namespace,
                                    StringComparison.Ordinal) &&
                                string.Equals(
                                    x.Key,
                                    draftEntry.Key,
                                    StringComparison.Ordinal));

                    if (entry is null || (draftEntry.Source is not null && entry.Original != draftEntry.Source))
                    {
                        _blockedRecovery.Add(new DraftFileState(fullPath, [draftEntry]));
                        SaveTransactionService.Record("draft-blocked", fullPath, draftEntry.Namespace + ":" + draftEntry.Key);
                        continue;
                    }

                    if (draftEntry.EditedNamespace is not null) entry.Namespace = draftEntry.EditedNamespace;
                    if (draftEntry.EditedKey is not null) entry.Key = draftEntry.EditedKey;
                    if (draftEntry.EditedOriginal is not null) entry.Original = draftEntry.EditedOriginal;

                    entry.Translation =
                        draftEntry.Translation;

                    _restoredDraftEntries++;
                    SaveTransactionService.Record("draft-recovered", fullPath, draftEntry.Namespace + ":" + draftEntry.Key);
                }
            }

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

            pendingSession = session;
            BusyText = "Подготовка таблицы…";
            foreach (var entry in document.Entries)
            {
                if ((entry.Index & 4095) == 0)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    _operationCancellation!.Token.ThrowIfCancellationRequested();
                    BusyPercent = 100.0 * entry.Index / Math.Max(1, document.Entries.Count);
                }
                entry.PropertyChanged += Entry_PropertyChanged;
                _entrySessions[entry] = session;

                entry.RefreshValidation();

                var status = entry.Status;
                session.KnownStatuses[entry] = status;
                session.KnownTranslations[entry] = entry.Translation;
                foreach (var field in Enum.GetValues<EntryField>()) session.KnownFields[(entry, field)] = entry.GetField(field);
                session.KnownValidationStates[entry] = entry.HasValidationIssues;

                if (entry.HasValidationIssues)
                    session.ValidationErrorCount++;

                ChangeStatusCounter(
                    session,
                    status,
                    1);
            }

            _sessions[fullPath] = session;
            pendingSession = null;
            OpenTabs.Add(node);
            ActivateSession(session);
            RaiseGlobalCommandStates();
            ScheduleWorkspaceSave();
        }
        catch (OperationCanceledException) { IssueLogService.Record("Открытие отменено: " + fullPath); }
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
            if (pendingSession is not null)
            {
                foreach (var entry in pendingSession.Document.Entries) { entry.PropertyChanged -= Entry_PropertyChanged; _entrySessions.Remove(entry); }
                _entrySessions.TrimExcess();
            }
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

        foreach (var node in FileTree)
            SetActiveNode(node, session.Node);

        Entries.ReplaceAll(
            session.Document.Entries);

        EntriesView.Refresh();

        SelectedEntry =
            session.SelectedEntry
            ?? EntriesView
                .Cast<LocalizationEntry>()
                .FirstOrDefault();

        RefreshNamespaces();
        RefreshConsistency();
        RaiseStatsChanged();
        RaiseHistoryCommandStates();
        RaiseGlobalCommandStates();
        NextUntranslatedCommand.RaiseCanExecuteChanged();
        PreviousUntranslatedCommand.RaiseCanExecuteChanged();
        ReplaceAllCommand.RaiseCanExecuteChanged();
        ScheduleWorkspaceSave();
    }

    private static void SetActiveNode(
        FileNode node,
        FileNode activeNode)
    {
        node.IsActive =
            ReferenceEquals(node, activeNode);

        foreach (var child in node.Children)
            SetActiveNode(child, activeNode);
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
                ScheduleWorkspaceSave();
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
                         .Where(x => !File.GetAttributes(x).HasFlag(FileAttributes.ReparsePoint) && Path.GetFileName(x) != ".git" && Path.GetFileName(x) != ".localization-backups")
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
                        IsDirectory = false
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
        var annotations = Annotations.Capture(session.Document);
        FileSafetyService.CheckUnchanged(session.Document);
        BackupService.CreateBackup(
            session.Document.FilePath);

        await _adapter.SaveAsync(
            session.Document, _operationCancellation?.Token ?? default, OperationProgress());

        Annotations.Migrate(session.Document.FilePath, annotations);
        BackupService.Prune(session.Document.FilePath, Settings.BackupLimit);
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
        ScheduleWorkspaceSave();
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
            OpenTabs.Remove(node);
            node.IsActive = false;

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
        ScheduleWorkspaceSave();

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

    public void GoTo(string query)
    {
        var session = _activeSession;

        if (session is null ||
            string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        query = query.Trim();

        LocalizationEntry? target = null;

        if (int.TryParse(query, out var row))
        {
            target =
                session.Document.Entries
                    .FirstOrDefault(x => x.Index == row);
        }

        target ??=
            session.Document.Entries
                .FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Key,
                            query,
                            StringComparison.OrdinalIgnoreCase));

        if (target is null &&
            query.Contains(':'))
        {
            var separator = query.IndexOf(':');
            var ns = query[..separator].Trim();
            var key = query[(separator + 1)..].Trim();

            target =
                session.Document.Entries
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Namespace,
                                ns,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                x.Key,
                                key,
                                StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
        {
            AppDialog.Show(
                $"Строка или ключ «{query}» не найдены.",
                "Переход",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        StatusFilter = "Все";
        NamespaceFilter = null;
        SearchText = string.Empty;
        EntriesView.Refresh();

        SelectedEntry = target;
        EntriesView.MoveCurrentTo(target);
    }

    private void ReplaceCurrent()
    {
        var entry = SelectedEntry;

        if (entry is null ||
            string.IsNullOrEmpty(SearchText) ||
            !entry.Translation.Contains(
                SearchText,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        entry.Translation =
            entry.Translation.Replace(
                SearchText,
                ReplaceText,
                StringComparison.OrdinalIgnoreCase);
    }

    private void ReplaceAll()
    {
        var session = _activeSession;

        if (session is null ||
            string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        var count = 0;

        foreach (var entry in session.Document.Entries)
        {
            if (NamespaceFilter is not null &&
                !string.Equals(
                    entry.Namespace,
                    NamespaceFilter,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entry.Translation.Contains(
                    SearchText,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.Translation =
                entry.Translation.Replace(
                    SearchText,
                    ReplaceText,
                    StringComparison.OrdinalIgnoreCase);

            count++;
        }

        AppDialog.Show(
            $"Заменено строк: {count}.",
            "Найти и заменить",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void NavigateUntranslated(int direction)
    {
        var session = _activeSession;

        if (session is null)
            return;

        var candidates =
            session.Document.Entries
                .Where(
                    x =>
                        x.Status == TranslationStatus.Untranslated &&
                        (string.IsNullOrWhiteSpace(NamespaceFilter) ||
                         string.Equals(
                             x.Namespace,
                             NamespaceFilter,
                             StringComparison.OrdinalIgnoreCase)))
                .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(
                "Непереведённых строк в текущей области нет.",
                "Навигация",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var currentIndex =
            SelectedEntry is null
                ? -1
                : candidates.IndexOf(SelectedEntry);

        var nextIndex =
            direction > 0
                ? (currentIndex + 1 + candidates.Count) % candidates.Count
                : (currentIndex <= 0 ? candidates.Count - 1 : currentIndex - 1);

        StatusFilter = "Все";
        SearchText = string.Empty;
        EntriesView.Refresh();

        var target = candidates[nextIndex];
        SelectedEntry = target;
        EntriesView.MoveCurrentTo(target);
    }

    private void ApplyCurrent()
    {
        var index = SelectedEntry?.Index ?? _lastSelectedIndex;
        EntriesView.Refresh();
        SelectedEntry = EntriesView.Cast<LocalizationEntry>().FirstOrDefault(x => x.Index > index)
            ?? EntriesView.Cast<LocalizationEntry>().LastOrDefault();
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

        if (NotesOnly && GetAnnotation(entry) is null) return false;
        if (!SmartFilters.Matches(entry)) return false;

        if (StatusFilter == "Несогласованные")
        {
            if (!_inconsistentSources.Contains(entry.Original)) return false;
        }
        else if (StatusFilter == "Ошибки")
        {
            if (!entry.HasValidationIssues)
                return false;
        }
        else if (StatusFilter != "Все" &&
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

        var pending = session.UndoStack.Peek();
        if (pending.Batch is not null)
        {
            try { ReplayBatch(pending, false); } catch (Exception e) { AppDialog.Show(e.Message, "Отмена операции"); }
            return;
        }
        var edit = session.UndoStack.Pop();

        session.HistoryChangeInProgress = true;

        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.SetField(edit.Field, edit.Before);
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

        var pending = session.RedoStack.Peek();
        if (pending.Batch is not null)
        {
            try { ReplayBatch(pending, true); } catch (Exception e) { AppDialog.Show(e.Message, "Повтор операции"); }
            return;
        }
        var edit = session.RedoStack.Pop();

        session.HistoryChangeInProgress = true;

        try
        {
            SelectedEntry = edit.Entry;
            edit.Entry.SetField(edit.Field, edit.After);
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
        if (!Enum.TryParse<EntryField>(e.PropertyName, out var editedField) ||
            sender is not LocalizationEntry entry ||
            !_entrySessions.TryGetValue(
                entry,
                out var session))
        {
            return;
        }

        if (ReferenceEquals(entry, SelectedEntry)) { OnPropertyChanged(nameof(SelectedContextText)); if (editedField is EntryField.Original or EntryField.Namespace) OnPropertyChanged(nameof(GlossaryHints)); }
        _namespaceCache.Remove(session.Document);
        if (editedField is EntryField.Translation or EntryField.Original) _consistencyDirty = true;
        entry.RefreshValidation();

        var currentValidation =
            entry.HasValidationIssues;

        if (!session.KnownValidationStates.TryGetValue(
                entry,
                out var previousValidation))
        {
            previousValidation = currentValidation;
        }

        if (previousValidation != currentValidation)
        {
            session.ValidationErrorCount +=
                currentValidation ? 1 : -1;

            session.KnownValidationStates[entry] =
                currentValidation;
        }

        var currentTranslation = entry.GetField(editedField);

        if (!session.KnownFields.TryGetValue((entry, editedField), out var previousTranslation))
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
                    currentTranslation, editedField));

            session.RedoStack.Clear();
            RecordHistory([session.UndoStack.Peek()], "Правка строки");
        }

        session.KnownFields[(entry, editedField)] = currentTranslation;
        session.KnownTranslations[entry] = entry.Translation;

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

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
        RaiseGlobalCommandStates();
        ScheduleWorkspaceSave();
    }

    private static void RebuildStatusCache(
        DocumentSession session)
    {
        session.KnownStatuses.Clear();
        session.KnownTranslations.Clear();
        session.KnownFields.Clear();
        session.KnownValidationStates.Clear();
        session.TranslatedCount = 0;
        session.UntranslatedCount = 0;
        session.ModifiedCount = 0;
        session.ValidationErrorCount = 0;

        foreach (var entry in
                 session.Document.Entries)
        {
            entry.RefreshValidation();

            var status =
                entry.Status;

            session.KnownStatuses[entry] =
                status;

            session.KnownTranslations[entry] = entry.Translation;
            foreach (var field in Enum.GetValues<EntryField>()) session.KnownFields[(entry, field)] = entry.GetField(field);

            session.KnownValidationStates[entry] =
                entry.HasValidationIssues;

            if (entry.HasValidationIssues)
                session.ValidationErrorCount++;

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
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void RaiseGlobalCommandStates()
    {
        SaveCommand.RaiseCanExecuteChanged();
        SaveAllCommand.RaiseCanExecuteChanged();
        CloseFileCommand.RaiseCanExecuteChanged();
        ReplaceAllCommand.RaiseCanExecuteChanged();
        NextUntranslatedCommand.RaiseCanExecuteChanged();
        PreviousUntranslatedCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
