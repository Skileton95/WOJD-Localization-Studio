using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ILocalizationFileAdapter _adapter = new NdjsonLocalizationAdapter();
    private LocalizationDocument? _document;
    private LocalizationEntry? _selectedEntry;
    private string _searchText = string.Empty;
    private string _statusFilter = "Все";
    private bool _isBusy;

    public MainViewModel()
    {
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        OpenFileCommand = new RelayCommand(async () => await OpenFileAsync());
        OpenFolderCommand = new RelayCommand(async () => await OpenFolderAsync());
        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => _document is not null && HasUnsavedChanges);
        ApplyCommand = new RelayCommand(() => ApplyCurrent(), () => SelectedEntry is not null);
        PreviousCommand = new RelayCommand(() => MoveSelection(-1), () => SelectedEntry is not null);
        NextCommand = new RelayCommand(() => MoveSelection(1), () => SelectedEntry is not null);
    }

    public ObservableCollection<LocalizationEntry> Entries { get; } = new();
    public ObservableCollection<FileNode> FileTree { get; } = new();
    public ICollectionView EntriesView { get; }
    public string[] StatusFilters { get; } = ["Все", "Без перевода", "Переведено", "Изменено"];
    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }

    public LocalizationEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                PreviousCommand.RaiseCanExecuteChanged();
                NextCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) EntriesView.Refresh();
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value)) EntriesView.Refresh();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public int TotalCount => Entries.Count;
    public int TranslatedCount => Entries.Count(x => x.Status == TranslationStatus.Translated);
    public int UntranslatedCount => Entries.Count(x => x.Status == TranslationStatus.Untranslated);
    public int ModifiedCount => Entries.Count(x => x.Status == TranslationStatus.Modified);
    public bool HasUnsavedChanges => ModifiedCount > 0;

    public async Task LoadPathAsync(string path)
    {
        if (!_adapter.CanOpen(path))
        {
            MessageBox.Show("Пока подключён базовый адаптер NDJSON/JSONL. Реальный формат русификатора будет добавлен отдельным адаптером.", "Формат файла", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDiscardUnsaved()) return;

        IsBusy = true;
        try
        {
            UnsubscribeEntries();
            _document = await _adapter.LoadAsync(path);
            Entries.Clear();
            foreach (var entry in _document.Entries)
            {
                entry.PropertyChanged += Entry_PropertyChanged;
                Entries.Add(entry);
            }
            EntriesView.Refresh();
            SelectedEntry = Entries.FirstOrDefault();
            RefreshStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка открытия", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool ConfirmDiscardUnsaved()
    {
        if (!HasUnsavedChanges) return true;
        return MessageBox.Show("Есть несохранённые изменения. Продолжить без сохранения?", "Несохранённые изменения", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "NDJSON/JSONL (*.ndjson;*.jsonl)|*.ndjson;*.jsonl|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        await LoadPathAsync(dialog.FileName);

        if (_document is not null &&
            string.Equals(_document.FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase))
        {
            FileTree.Clear();
            FileTree.Add(new FileNode
            {
                Name = Path.GetFileName(dialog.FileName),
                FullPath = dialog.FileName,
                IsDirectory = false,
                EntryCount = Entries.Count
            });
        }
    }

    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true) return;

        FileTree.Clear();
        var root = BuildTree(dialog.FolderName);
        FileTree.Add(root);

        var first = FindFirstSupported(root);
        if (first is not null) await LoadPathAsync(first.FullPath);
    }

    private FileNode BuildTree(string path)
    {
        var node = new FileNode { Name = Path.GetFileName(path), FullPath = path, IsDirectory = true };
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(x => x)) node.Children.Add(BuildTree(dir));
            foreach (var file in Directory.EnumerateFiles(path).Where(_adapter.CanOpen).OrderBy(x => x))
            {
                node.Children.Add(new FileNode
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false,
                    EntryCount = CountFileRows(file)
                });
            }
        }
        catch { }
        return node;
    }

    private static int CountFileRows(string path)
    {
        try
        {
            return File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line));
        }
        catch
        {
            return 0;
        }
    }

    private static FileNode? FindFirstSupported(FileNode node)
    {
        if (!node.IsDirectory) return node;
        foreach (var child in node.Children)
        {
            var found = FindFirstSupported(child);
            if (found is not null) return found;
        }
        return null;
    }

    private async Task SaveAsync()
    {
        if (_document is null) return;
        try
        {
            BackupService.CreateBackup(_document.FilePath);
            await _adapter.SaveAsync(_document);
            RefreshStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyCurrent()
    {
        EntriesView.Refresh();
        RefreshStats();
        MoveSelection(1);
    }

    private void MoveSelection(int delta)
    {
        if (SelectedEntry is null) return;
        var visible = EntriesView.Cast<LocalizationEntry>().ToList();
        var current = visible.IndexOf(SelectedEntry);
        if (current < 0) return;
        var next = Math.Clamp(current + delta, 0, visible.Count - 1);
        SelectedEntry = visible[next];
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not LocalizationEntry entry) return false;
        var statusOk = StatusFilter == "Все" || entry.StatusText == StatusFilter;
        if (!statusOk) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return entry.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Original.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.Translation.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocalizationEntry.Translation) or nameof(LocalizationEntry.Status))
        {
            RefreshStats();
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(UntranslatedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        EntriesView.Refresh();
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void UnsubscribeEntries()
    {
        foreach (var entry in Entries) entry.PropertyChanged -= Entry_PropertyChanged;
    }
}
