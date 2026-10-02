using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class ProjectToolsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private List<NamespaceStat> _allNamespaceStats = [];

    public ProjectToolsWindow(
        MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        RefreshAll();

        Closed += (_, _) =>
        {
            _viewModel.SaveProjectMetadata();
        };
    }

    private void RefreshAll()
    {
        var documents =
            _viewModel.GetOpenDocuments();

        ProjectSubtitle.Text =
            $"{_viewModel.ProjectName} • открыто файлов: {documents.Count}";

        _allNamespaceStats =
            ProjectAnalysisService.BuildNamespaceStats(
                documents,
                _viewModel.ProjectMetadata.FavoriteNamespaces);

        ApplyNamespaceSearch();

        SavedFiltersList.ItemsSource =
            _viewModel.ProjectMetadata.SavedFilters.ToList();

        ConsistencyGrid.ItemsSource =
            ProjectAnalysisService.FindConsistencyIssues(
                documents);

        LoadCurrentNote();

        var stats =
            ProjectAnalysisService.BuildStatistics(
                documents);

        StatisticsText.Text =
            $"Файлов: {stats.Files:N0}{Environment.NewLine}" +
            $"Строк: {stats.Entries:N0}{Environment.NewLine}" +
            $"Переведено: {stats.Translated:N0}{Environment.NewLine}" +
            $"Без перевода: {stats.Untranslated:N0}{Environment.NewLine}" +
            $"Изменено: {stats.Modified:N0}{Environment.NewLine}" +
            $"QA-ошибок: {stats.QaErrors:N0}{Environment.NewLine}" +
            $"Namespace: {stats.Namespaces:N0}{Environment.NewLine}" +
            $"Уникальных оригиналов: {stats.UniqueSources:N0}{Environment.NewLine}" +
            $"Повторяющихся оригиналов: {stats.DuplicateSources:N0}";

        HistoryGrid.ItemsSource =
            _viewModel.ProjectRoot is null
                ? []
                : ChangeHistoryService.Read(
                    _viewModel.ProjectRoot,
                    5000);

        ReviewGrid.ItemsSource =
            ProjectAnalysisService.BuildReviewQueue(
                documents,
                _viewModel.ProjectMetadata);

        GlossaryGrid.ItemsSource =
            _viewModel.ProjectMetadata.Glossary.ToList();

        ConflictsGrid.ItemsSource =
            ProjectSyncService.FindConflicts(
                documents);

        AssignmentsGrid.ItemsSource =
            _viewModel.ProjectMetadata.Assignments.ToList();

        RefreshContext();

        var profile =
            _viewModel.ProjectProfile;

        ProjectNameBox.Text =
            profile?.Name
            ?? _viewModel.ProjectName;

        ProjectRootBox.Text =
            profile?.RootPath
            ?? _viewModel.ProjectRoot
            ?? string.Empty;

        NativeLanguageBox.Text =
            profile?.NativeLanguage
            ?? "zh-Hans";

        TargetLanguageBox.Text =
            profile?.TargetLanguage
            ?? "ru-RU";

        SourceLocresBox.Text =
            profile?.SourceLocresPath
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                ReleaseVersionBox.Text))
        {
            ReleaseVersionBox.Text =
                DateTime.Now.ToString("yyyy.MM.dd");
        }
    }

    private void RefreshAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshAll();
    }

    private void NamespaceSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ApplyNamespaceSearch();
    }

    private void ApplyNamespaceSearch()
    {
        var query =
            NamespaceSearchBox?.Text?.Trim()
            ?? string.Empty;

        NamespaceGrid.ItemsSource =
            string.IsNullOrWhiteSpace(query)
                ? _allNamespaceStats
                : _allNamespaceStats
                    .Where(x =>
                        x.Namespace.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
    }

    private void NamespaceReset_Click(
        object sender,
        RoutedEventArgs e)
    {
        NamespaceSearchBox.Text =
            string.Empty;

        _viewModel.ClearSmartFilterCommand.Execute(null);
        _viewModel.FilterAllCommand.Execute(null);
    }

    private NamespaceStat? SelectedNamespace
        => NamespaceGrid.SelectedItem
            as NamespaceStat;

    private void NamespaceGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        FilterNamespace_Click(
            sender,
            new RoutedEventArgs());
    }

    private void FilterNamespace_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedNamespace is not { } item)
            return;

        _viewModel.FilterToNamespace(
            item.Namespace);
    }

    private void ToggleFavoriteNamespace_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SelectedNamespace is not { } item)
            return;

        _viewModel.ToggleFavoriteNamespace(
            item.Namespace);

        RefreshAll();
    }

    private SavedSmartFilter BuildSmartFilter()
        => new(
            string.IsNullOrWhiteSpace(
                FilterNameBox.Text)
                ? "Быстрый фильтр"
                : FilterNameBox.Text.Trim(),
            FilterUntranslated.IsChecked == true,
            FilterQa.IsChecked == true,
            FilterEmptySource.IsChecked == true,
            FilterCjk.IsChecked == true,
            FilterPlaceholders.IsChecked == true,
            FilterLong.IsChecked == true,
            string.IsNullOrWhiteSpace(
                FilterNamespaceBox.Text)
                ? null
                : FilterNamespaceBox.Text.Trim());

    private void ApplySmartFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.ApplySmartFilterCommand.Execute(
            BuildSmartFilter());
    }

    private void SaveSmartFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filter =
            BuildSmartFilter();

        var existing =
            _viewModel.ProjectMetadata.SavedFilters
                .FindIndex(x =>
                    string.Equals(
                        x.Name,
                        filter.Name,
                        StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            _viewModel.ProjectMetadata.SavedFilters[
                existing] = filter;
        }
        else
        {
            _viewModel.ProjectMetadata.SavedFilters.Add(
                filter);
        }

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void ClearSmartFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.ClearSmartFilterCommand.Execute(null);
    }

    private void SavedFiltersList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (SavedFiltersList.SelectedItem
            is SavedSmartFilter filter)
        {
            _viewModel.ApplySmartFilterCommand.Execute(
                filter);
        }
    }

    private void ApplyConsistency_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ConsistencyGrid.SelectedItem
                is not ConsistencyIssue issue ||
            string.IsNullOrWhiteSpace(
                ConsistencyTranslationBox.Text))
        {
            return;
        }

        _viewModel.ApplyConsistencyTranslation(
            issue,
            ConsistencyTranslationBox.Text);

        RefreshAll();
    }

    private void LoadCurrentNote()
    {
        var entry =
            _viewModel.SelectedEntry;

        var document =
            _viewModel.ActiveDocument;

        if (entry is null ||
            document is null)
        {
            NoteIdentityText.Text =
                "Строка не выбрана";

            NoteTextBox.Text =
                string.Empty;

            NoteStateBox.SelectedIndex = 0;
            return;
        }

        NoteIdentityText.Text =
            $"{Path.GetFileName(document.FilePath)} • {entry.Namespace}:{entry.Key}";

        var id =
            StudioProjectMetadataService.EntryId(
                document.FilePath,
                entry.Namespace,
                entry.Key);

        if (_viewModel.ProjectMetadata.Notes.TryGetValue(
                id,
                out var note))
        {
            NoteTextBox.Text =
                note.Text;

            SelectComboContent(
                NoteStateBox,
                note.State);
        }
        else
        {
            NoteTextBox.Text =
                string.Empty;

            NoteStateBox.SelectedIndex = 0;
        }
    }

    private void SaveNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entry =
            _viewModel.SelectedEntry;

        var document =
            _viewModel.ActiveDocument;

        if (entry is null ||
            document is null)
        {
            return;
        }

        var state =
            (NoteStateBox.SelectedItem
                as ComboBoxItem)
                ?.Content?.ToString()
            ?? "Черновик";

        var id =
            StudioProjectMetadataService.EntryId(
                document.FilePath,
                entry.Namespace,
                entry.Key);

        if (string.IsNullOrWhiteSpace(
                NoteTextBox.Text))
        {
            _viewModel.ProjectMetadata.Notes.Remove(
                id);
        }
        else
        {
            _viewModel.ProjectMetadata.Notes[id] =
                new EntryNote(
                    NoteTextBox.Text,
                    state,
                    Environment.UserName,
                    DateTime.UtcNow);
        }

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void ExportStatistics_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "wojd-project-statistics.csv"
            };

        if (dialog.ShowDialog(this) != true)
            return;

        var sb =
            new StringBuilder();

        sb.AppendLine(
            "Namespace,Total,Translated,Untranslated,QA,Progress");

        foreach (var row in _allNamespaceStats)
        {
            sb.AppendLine(
                $"{Csv(row.Namespace)},{row.Total},{row.Translated},{row.Untranslated},{row.Errors},{row.ProgressPercent:F2}");
        }

        File.WriteAllText(
            dialog.FileName,
            sb.ToString(),
            new UTF8Encoding(true));
    }

    private void RestoreHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem
            is not ChangeHistoryRecord record)
        {
            return;
        }

        _viewModel.RestoreHistoryRecord(
            record);

        RefreshAll();
    }

    private void ReviewGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        OpenReview_Click(
            sender,
            new RoutedEventArgs());
    }

    private void OpenReview_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ReviewGrid.SelectedItem
            is not ReviewQueueItem item)
        {
            return;
        }

        _viewModel.OpenEntry(
            item.FilePath,
            item.Entry);
    }

    private void AddGlossary_Click(
        object sender,
        RoutedEventArgs e)
    {
        var source =
            Prompt(
                "Глоссарий",
                "Исходный термин:");

        if (source is null)
            return;

        var translation =
            Prompt(
                "Глоссарий",
                "Утверждённый перевод:");

        if (translation is null)
            return;

        var category =
            Prompt(
                "Глоссарий",
                "Категория:",
                "Общее")
            ?? "Общее";

        var requirement =
            Prompt(
                "Глоссарий",
                "Правило: Обязательно / Желательно / Запрещено",
                "Обязательно")
            ?? "Обязательно";

        _viewModel.ProjectMetadata.Glossary.Add(
            new GlossaryTerm(
                source,
                translation,
                category,
                requirement,
                null));

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void RemoveGlossary_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GlossaryGrid.SelectedItem
            is not GlossaryTerm term)
        {
            return;
        }

        _viewModel.ProjectMetadata.Glossary.Remove(
            term);

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void RefreshConflicts_Click(
        object sender,
        RoutedEventArgs e)
    {
        ConflictsGrid.ItemsSource =
            ProjectSyncService.FindConflicts(
                _viewModel.GetOpenDocuments());
    }

    private void SyncOpenDocuments_Click(
        object sender,
        RoutedEventArgs e)
    {
        var target =
            _viewModel.ActiveDocument;

        if (target is null)
            return;

        var sources =
            _viewModel.GetOpenDocuments()
                .Where(x =>
                    !ReferenceEquals(
                        x,
                        target))
                .ToList();

        var summary =
            ProjectSyncService.SyncInto(
                target,
                sources,
                ProtectExistingTranslations.IsChecked == true);

        SyncStatusText.Text =
            $"Обновлено: {summary.Updated}; пропущено переведённых: {summary.SkippedTranslated}; конфликтов: {summary.Conflicts}.";

        RefreshAll();
    }

    private async void RefreshGit_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshGitAsync();
    }

    private async Task RefreshGitAsync()
    {
        if (_viewModel.ProjectRoot is null)
        {
            GitStatusBox.Text =
                "Проект не выбран.";

            return;
        }

        var result =
            await GitService.StatusAsync(
                _viewModel.ProjectRoot);

        GitStatusBox.Text =
            result.Success
                ? result.Output
                : result.Error;
    }

    private async void CommitGit_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.ProjectRoot is null ||
            string.IsNullOrWhiteSpace(
                GitCommitMessageBox.Text))
        {
            return;
        }

        var result =
            await GitService.CommitAllAsync(
                _viewModel.ProjectRoot,
                GitCommitMessageBox.Text.Trim());

        GitStatusBox.Text =
            result.Success
                ? result.Output
                : result.Error;

        if (result.Success)
            GitCommitMessageBox.Text = string.Empty;
    }

    private async void CreateReleasePackage_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.ProjectRoot is null)
            return;

        var dialog =
            new SaveFileDialog
            {
                Filter = "ZIP (*.zip)|*.zip",
                FileName =
                    $"WOJD-RU-{ReleaseVersionBox.Text.Trim()}.zip"
            };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path =
                await ReleasePackageService.CreateAsync(
                    _viewModel.ProjectRoot,
                    dialog.FileName,
                    ReleaseVersionBox.Text.Trim(),
                    _viewModel.GetOpenDocuments()
                        .Select(x => x.FilePath),
                    ReleaseChangelogBox.Text);

            AppDialog.Show(
                $"Пакет создан:{Environment.NewLine}{path}",
                "Релиз",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка создания релиза",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                this);
        }
    }

    private void SaveProjectProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                ProjectRootBox.Text))
        {
            return;
        }

        var profile =
            new ProjectProfile(
                string.IsNullOrWhiteSpace(
                    ProjectNameBox.Text)
                    ? "WOJD Localization"
                    : ProjectNameBox.Text.Trim(),
                Path.GetFullPath(
                    ProjectRootBox.Text.Trim()),
                string.IsNullOrWhiteSpace(
                    NativeLanguageBox.Text)
                    ? "zh-Hans"
                    : NativeLanguageBox.Text.Trim(),
                string.IsNullOrWhiteSpace(
                    TargetLanguageBox.Text)
                    ? "ru-RU"
                    : TargetLanguageBox.Text.Trim(),
                string.IsNullOrWhiteSpace(
                    SourceLocresBox.Text)
                    ? null
                    : SourceLocresBox.Text.Trim(),
                ["*.ndjson", "*.jsonl", "*.locres"]);

        _viewModel.SaveProjectProfile(
            profile);

        ProjectActionStatus.Text =
            $"Профиль сохранён: {ProjectProfileService.FileName}";

        RefreshAll();
    }

    private async void RebuildIndex_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProjectActionStatus.Text =
            "Перестроение индекса…";

        try
        {
            var path =
                await _viewModel.RebuildProjectIndexAsync();

            ProjectActionStatus.Text =
                path is null
                    ? "Сначала создайте профиль проекта."
                    : $"SQLite-индекс: {path}";
        }
        catch (Exception ex)
        {
            ProjectActionStatus.Text =
                ex.Message;
        }
    }

    private void AddAssignment_Click(
        object sender,
        RoutedEventArgs e)
    {
        var ns =
            Prompt(
                "Назначение",
                "Namespace:");

        if (ns is null)
            return;

        var assignee =
            Prompt(
                "Назначение",
                "Исполнитель:");

        if (assignee is null)
            return;

        _viewModel.ProjectMetadata.Assignments.Add(
            new AssignmentRecord(
                ns,
                assignee,
                "Черновик"));

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void ExportAssignments_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "wojd-assignments.json"
            };

        if (dialog.ShowDialog(this) != true)
            return;

        CollaborationService.ExportAssignments(
            dialog.FileName,
            _viewModel.ProjectMetadata.Assignments);
    }

    private void ImportAssignments_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json"
            };

        if (dialog.ShowDialog(this) != true)
            return;

        var imported =
            CollaborationService.ImportAssignments(
                dialog.FileName);

        _viewModel.ProjectMetadata.Assignments.Clear();
        _viewModel.ProjectMetadata.Assignments.AddRange(
            imported);

        _viewModel.SaveProjectMetadata();
        RefreshAll();
    }

    private void RefreshContext()
    {
        var selected =
            _viewModel.SelectedEntry;

        var document =
            _viewModel.ActiveDocument;

        ContextGrid.ItemsSource =
            selected is null ||
            document is null
                ? []
                : ProjectAnalysisService.BuildContext(
                    selected,
                    document.FilePath,
                    _viewModel.GetOpenDocuments());
    }

    private void ContextGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ContextGrid.SelectedItem
            is not ContextEntry item)
        {
            return;
        }

        _viewModel.OpenEntry(
            item.FilePath,
            item.Entry);
    }

    private string? Prompt(
        string title,
        string prompt,
        string initial = "")
    {
        var dialog =
            new TextInputDialog(
                title,
                prompt,
                initial)
            {
                Owner = this
            };

        return dialog.ShowDialog() == true
            ? dialog.Value.Trim()
            : null;
    }

    private static string Csv(string value)
        => "\"" +
           value.Replace(
               "\"",
               "\"\"") +
           "\"";

    private static void SelectComboContent(
        ComboBox comboBox,
        string content)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(
                    comboItem.Content?.ToString(),
                    content,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem =
                    comboItem;

                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }
}
