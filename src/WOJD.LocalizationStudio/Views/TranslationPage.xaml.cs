using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class TranslationPage : UserControl
{
    private MainViewModel? _viewModel;
    private LocalizationEntry? _currentEntry;
    private string _baselineTranslation = string.Empty;
    private readonly ExternalFileMonitorService _monitor = new();
    private LocalizationDocument? _watchedDocument;
    private ExternalFileDiffResult? _externalDiff;
    private CancellationTokenSource? _operationCts;
    private bool _attached;

    public TranslationPage()
    {
        InitializeComponent();
        _monitor.Changed += Monitor_Changed;
        DataObject.AddPastingHandler(TranslationBox, TranslationBox_Pasting);
    }

    public void Attach(MainViewModel viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            _attached = true;
            RefreshSelection();
            return;
        }

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _attached = true;
        ApplySettings();
        RefreshSelection();
    }

    public void DisposePage()
    {
        _operationCts?.Cancel();
        CaptureManualHistory();
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _monitor.Dispose();
        _attached = false;
    }

    public void ApplySettings()
    {
        var settings = EditorSettingsService.Current;
        TranslationBox.FontSize = settings.EditorFontSize;
        TranslationBox.TextWrapping = settings.WrapTranslation
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    public void CommitTranslation()
        => TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    public void ScrollToSelected()
    {
        if (_viewModel?.SelectedEntry is null)
            return;

        EntriesGrid.UpdateLayout();
        EntriesGrid.SelectedItem = _viewModel.SelectedEntry;
        EntriesGrid.ScrollIntoView(_viewModel.SelectedEntry);
    }

    public void NavigateQaError(int direction)
    {
        if (_viewModel is null)
            return;

        if (_viewModel.FilterAllCommand.CanExecute(null))
            _viewModel.FilterAllCommand.Execute(null);

        _viewModel.SearchText = string.Empty;
        var errors = _viewModel.GetErrorEntries().ToList();
        if (errors.Count == 0)
        {
            AppDialog.Show(
                "QA-ошибок в текущем файле нет.",
                "QA",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                Window.GetWindow(this));
            return;
        }

        var currentIndex = _viewModel.SelectedEntry is null
            ? -1
            : errors.IndexOf(_viewModel.SelectedEntry);
        var nextIndex = direction > 0
            ? (currentIndex + 1 + errors.Count) % errors.Count
            : (currentIndex <= 0 ? errors.Count - 1 : currentIndex - 1);

        _viewModel.SelectedEntry = errors[nextIndex];
        ScrollToSelected();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEntry))
        {
            CaptureManualHistory();
            RefreshSelection();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.TranslatedCount)
            or nameof(MainViewModel.UntranslatedCount)
            or nameof(MainViewModel.ModifiedCount)
            or nameof(MainViewModel.ErrorCount))
        {
            RefreshFileInfo();
        }
    }

    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel?.SelectedEntry is not null)
            EntriesGrid.ScrollIntoView(_viewModel.SelectedEntry);
    }

    private void RefreshSelection()
    {
        _currentEntry = _viewModel?.SelectedEntry;
        _baselineTranslation = _currentEntry?.Translation ?? string.Empty;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

        EntryMetaText.Text = _currentEntry is null
            ? string.Empty
            : $"Namespace: {_currentEntry.Namespace}  ·  ID: {_currentEntry.Index:N0}";

        var document = _viewModel?.ActiveDocument;
        if (document is not null && !ReferenceEquals(document, _watchedDocument))
        {
            _watchedDocument = document;
            _monitor.Watch(document.FilePath);
            _externalDiff = null;
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
        }

        RefreshFileInfo();
        RefreshCharacterCount();
        RefreshDiagnostics();
    }

    private void RefreshFileInfo()
    {
        var document = _viewModel?.ActiveDocument;
        FileInfoText.Text = document is null
            ? string.Empty
            : Path.GetFileName(document.FilePath);

        if (document is null || _currentEntry is null)
        {
            PositionText.Text = string.Empty;
            return;
        }

        var position = document.Entries.IndexOf(_currentEntry);
        PositionText.Text = position >= 0
            ? $"{position + 1:N0} / {document.Entries.Count:N0}"
            : $"{document.Entries.Count:N0} строк";
    }

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_attached)
            return;

        RefreshCharacterCount();
        RefreshDiagnostics();
    }

    private void TranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitTranslation();
        CaptureManualHistory();
        RefreshDiagnostics();
    }

    private void RefreshCharacterCount()
        => CharacterCountText.Text = $"{TranslationBox.Text.Length:N0} символов";

    private void CaptureManualHistory()
    {
        if (_viewModel is null || _currentEntry is null)
            return;

        CommitTranslation();
        var current = _currentEntry.Translation;
        if (string.Equals(_baselineTranslation, current, StringComparison.Ordinal))
            return;

        EntryHistoryService.Record(
            _currentEntry,
            _baselineTranslation,
            current,
            "Ручная правка");

        if (_viewModel.ActiveDocument is LocalizationDocument document)
        {
            ProjectHistoryService.Record(
                document.FilePath,
                "Ручная правка",
                1,
                _currentEntry.Key);
            TranslationMemoryService.Invalidate(document);
        }

        _baselineTranslation = current;
    }

    private void RefreshDiagnostics()
    {
        var entry = _viewModel?.SelectedEntry;
        if (entry is null)
        {
            StructureChip.Visibility = Visibility.Collapsed;
            TagsChip.Visibility = Visibility.Collapsed;
            PlaceholdersChip.Visibility = Visibility.Collapsed;
            NewLinesChip.Visibility = Visibility.Collapsed;
            RestoreStructureButton.Visibility = Visibility.Collapsed;
            AutoCorrectButton.IsEnabled = false;
            AutoCorrectButton.Content = "Автоисправить";
            QaDetailsText.Text = string.Empty;
            ContextQaText.Text = string.Empty;
            RefreshTranslationBorder(null);
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            StructureChip.Visibility = Visibility.Visible;
            StructureChipText.Text = "— Нет Original";
            SetChipState(StructureChip, StructureChipText, ChipState.Neutral);
            StructureChip.ToolTip = "Исходный текст отсутствует — структура не проверяется.";
            TagsChip.Visibility = Visibility.Collapsed;
            PlaceholdersChip.Visibility = Visibility.Collapsed;
            NewLinesChip.Visibility = Visibility.Collapsed;
            RestoreStructureButton.Visibility = Visibility.Collapsed;
            QaDetailsText.Text = entry.ValidationSummary;
            ContextQaText.Text = "QA: проверка структуры невозможна — Original отсутствует.";
            RefreshAutoCorrectButton(entry);
            RefreshTranslationBorder(null);
            return;
        }

        var diff = StructuralDiffService.Analyze(entry.Original, TranslationBox.Text);
        ConfigureQaChip(TagsChip, TagsChipText, "Теги", diff.TargetTags, diff.SourceTags,
            diff.SourceTags != diff.TargetTags || diff.MissingTokens.Any(IsTag) || diff.ExtraTokens.Any(IsTag) || diff.OrderMismatch,
            diff.SourceTags > 0 || diff.TargetTags > 0 || diff.MissingTokens.Any(IsTag) || diff.ExtraTokens.Any(IsTag));
        ConfigureQaChip(PlaceholdersChip, PlaceholdersChipText, "Плейсхолдеры", diff.TargetPlaceholders, diff.SourcePlaceholders,
            diff.SourcePlaceholders != diff.TargetPlaceholders || diff.MissingTokens.Any(x => !IsTag(x)) || diff.ExtraTokens.Any(x => !IsTag(x)),
            diff.SourcePlaceholders > 0 || diff.TargetPlaceholders > 0 || diff.MissingTokens.Any(x => !IsTag(x)) || diff.ExtraTokens.Any(x => !IsTag(x)));
        ConfigureQaChip(NewLinesChip, NewLinesChipText, "Переносы", diff.TargetNewLines, diff.SourceNewLines,
            diff.SourceNewLines != diff.TargetNewLines,
            diff.SourceNewLines > 0 || diff.TargetNewLines > 0 || diff.SourceNewLines != diff.TargetNewLines);

        StructureChip.Visibility = Visibility.Visible;
        StructureChipText.Text = diff.HasIssues ? "Структура ✕" : "Структура ✓";
        SetChipState(StructureChip, StructureChipText, diff.HasIssues ? ChipState.Error : ChipState.Ok);

        var details = new List<string>
        {
            $"Теги {diff.TargetTags}/{diff.SourceTags}",
            $"Плейсхолдеры {diff.TargetPlaceholders}/{diff.SourcePlaceholders}",
            $"Переносы {diff.TargetNewLines}/{diff.SourceNewLines}"
        };
        if (diff.MissingTokens.Count > 0)
            details.Add("Нет: " + string.Join(", ", diff.MissingTokens.Distinct()));
        if (diff.ExtraTokens.Count > 0)
            details.Add("Лишнее: " + string.Join(", ", diff.ExtraTokens.Distinct()));
        if (diff.OrderMismatch)
            details.Add("Нарушен порядок тегов");

        StructureChip.ToolTip = string.Join("\n", details);
        QaDetailsText.Text = string.IsNullOrWhiteSpace(entry.ValidationSummary)
            ? string.Join(" · ", details.Take(3))
            : entry.ValidationSummary;
        ContextQaText.Text = diff.HasIssues
            ? "QA: структурная ошибка. " + string.Join("; ", details.Skip(3))
            : entry.HasValidationIssues
                ? "QA: " + entry.ValidationSummary
                : "QA: ошибок не найдено.";

        var restore = StructureProtectionService.RestoreStructure(entry.Original, TranslationBox.Text);
        RestoreStructureButton.Visibility = diff.HasIssues && restore.CanRestore
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshAutoCorrectButton(entry);
        RefreshTranslationBorder(diff);
    }

    private static bool IsTag(string token) => token.StartsWith('<');

    private void ConfigureQaChip(
        Border border,
        TextBlock text,
        string label,
        int actual,
        int expected,
        bool issue,
        bool visible)
    {
        border.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            return;

        text.Text = issue ? $"{label} ✕ {actual}/{expected}" : $"{label} ✓";
        border.ToolTip = $"{label}: {actual}/{expected}";
        SetChipState(border, text, issue ? ChipState.Error : ChipState.Ok);
    }

    private static void SetChipState(Border border, TextBlock text, ChipState state)
    {
        switch (state)
        {
            case ChipState.Error:
                border.Background = new SolidColorBrush(Color.FromRgb(255, 240, 242));
                text.Foreground = Brushes.Firebrick;
                break;
            case ChipState.Ok:
                border.Background = new SolidColorBrush(Color.FromRgb(238, 249, 242));
                text.Foreground = new SolidColorBrush(Color.FromRgb(35, 138, 80));
                break;
            default:
                border.Background = new SolidColorBrush(Color.FromRgb(241, 244, 248));
                text.Foreground = Brushes.DimGray;
                break;
        }
    }

    private void RefreshAutoCorrectButton(LocalizationEntry entry)
    {
        var probe = new LocalizationEntry
        {
            Index = entry.Index,
            Namespace = entry.Namespace,
            Key = entry.Key,
            Original = entry.Original,
            TranslationField = entry.TranslationField
        };
        probe.Translation = TranslationBox.Text;
        var result = TranslationAutoCorrectionService.RuleBased.Correct(probe);
        AutoCorrectButton.IsEnabled = result.HasChanges;
        AutoCorrectButton.Content = result.HasChanges
            ? $"Автоисправить · {result.FixCount}"
            : "Автоисправить";
        AutoCorrectButton.ToolTip = result.HasChanges
            ? $"Найдено безопасных исправлений: {result.FixCount}"
            : result.RequiresReview
                ? result.ReviewReason
                : "Безопасных исправлений для этой строки нет";
    }

    private void RefreshTranslationBorder(StructuralDiffResult? diff)
    {
        if (TranslationBox.IsKeyboardFocusWithin)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 126, 247));
            return;
        }

        if (string.IsNullOrWhiteSpace(TranslationBox.Text))
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239));
            return;
        }

        if (diff?.HasIssues == true || _currentEntry?.HasStructuralValidationIssues == true)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 76, 85));
            return;
        }

        if (_currentEntry?.HasValidationIssues == true)
        {
            TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 154, 36));
            return;
        }

        TranslationBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 157, 91));
    }

    private void TranslationBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            string.IsNullOrWhiteSpace(entry.Original))
        {
            return;
        }

        string? proposed = null;
        if (e.Key == Key.Back)
            proposed = SimulateBackspace(TranslationBox);
        else if (e.Key == Key.Delete)
            proposed = SimulateDelete(TranslationBox);
        else if (e.Key == Key.X && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            proposed = ReplaceSelection(TranslationBox, string.Empty);

        if (proposed is null ||
            !StructureProtectionService.WouldWorsenStructure(entry.Original, TranslationBox.Text, proposed))
        {
            return;
        }

        System.Media.SystemSounds.Beep.Play();
        e.Handled = true;
        StructureChip.ToolTip = "Действие остановлено: оно повреждало тег или плейсхолдер.";
    }

    private void TranslationBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry ||
            string.IsNullOrWhiteSpace(entry.Original) ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            return;
        }

        var paste = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        var proposed = ReplaceSelection(TranslationBox, paste);
        if (!StructureProtectionService.WouldWorsenStructure(entry.Original, TranslationBox.Text, proposed))
            return;

        var answer = AppDialog.Show(
            "После вставки структура тегов/плейсхолдеров станет хуже.\n\nВставить всё равно?",
            "Защита структуры",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            Window.GetWindow(this));
        if (answer != MessageBoxResult.Yes)
            e.CancelCommand();
    }

    private static string ReplaceSelection(TextBox box, string value)
    {
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        return box.Text[..start] + value + box.Text[(start + length)..];
    }

    private static string SimulateBackspace(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);
        if (box.SelectionStart <= 0)
            return box.Text;
        return box.Text.Remove(box.SelectionStart - 1, 1);
    }

    private static string SimulateDelete(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);
        if (box.SelectionStart >= box.Text.Length)
            return box.Text;
        return box.Text.Remove(box.SelectionStart, 1);
    }

    private void RestoreStructure_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var before = entry.Translation;
        var result = StructureProtectionService.RestoreStructure(entry.Original, before);
        if (!result.Changed)
        {
            AppDialog.Show(
                result.Message,
                "Восстановление структуры",
                MessageBoxButton.OK,
                result.CanRestore ? MessageBoxImage.Information : MessageBoxImage.Warning,
                Window.GetWindow(this));
            return;
        }

        entry.Translation = result.Text;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, result.Text, "Восстановление структуры");
        RefreshDiagnostics();
    }

    private void AutoCorrect_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var before = entry.Translation;
        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
        if (!result.HasChanges)
            return;

        entry.Translation = result.CorrectedText;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, result.CorrectedText, "Автоисправление");
        RefreshDiagnostics();
    }

    private async void BulkAutoCorrect_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null)
            return;

        try
        {
            var entries = document.Entries.ToList();
            var fileName = Path.GetFileName(document.FilePath);
            var plan = await RunOperationAsync(
                $"Анализ файла {fileName}",
                (progress, token) => BackgroundCorrectionService.BuildPlanAsync(
                    entries,
                    TranslationAutoCorrectionService.RuleBased,
                    progress,
                    token));

            if (plan is null)
                return;

            if (plan.AffectedEntries == 0)
            {
                AppDialog.Show(
                    $"Текущий файл: {fileName}\nПроверено строк: {entries.Count:N0}. Изменений нет. Требуют ручной проверки: {plan.ReviewItems.Count:N0}.",
                    "Автоисправление файла",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    Window.GetWindow(this));
                return;
            }

            var preview = new TranslationCorrectionPreviewWindow(entries.Count, plan)
            {
                Owner = Window.GetWindow(this),
                Title = $"Автоисправление файла — {fileName}"
            };
            if (preview.ShowDialog() != true || preview.SelectedChanges.Count == 0)
                return;

            var snapshot = SnapshotService.CreateSnapshot(document, "before-bulk-autocorrect");
            var selected = preview.SelectedChanges.ToList();
            var changes = selected
                .Select(x => new BulkTextChange(x.Entry, x.Before, x.After))
                .ToList();
            TranslationCorrectionApplyResult applyResult;

            using (SuppressViewModelHistory())
                applyResult = plan.Apply(selected);

            BulkUndoService.Push(document.FilePath, "Автоисправление файла", changes);
            ProjectHistoryService.Record(
                document.FilePath,
                "Автоисправление файла",
                applyResult.AppliedEntries,
                $"Исправлений: {selected.Sum(x => x.FixCount):N0}; ручная проверка: {plan.ReviewItems.Count:N0}",
                snapshot.Path);
            TranslationMemoryService.Invalidate(document);
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _viewModel?.EntriesView.Refresh();
            RefreshDiagnostics();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка автоисправления файла",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                Window.GetWindow(this));
        }
    }

    private async Task<T?> RunOperationAsync<T>(
        string stage,
        Func<IProgress<BackgroundOperationProgress>, CancellationToken, Task<T>> action)
    {
        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        OperationPanel.Visibility = Visibility.Visible;
        OperationText.Text = stage;
        OperationProgress.Value = 0;

        var progress = new Progress<BackgroundOperationProgress>(p =>
        {
            OperationText.Text = $"{p.Stage}: {p.Current:N0} / {p.Total:N0}";
            OperationProgress.Value = p.Percent;
        });

        try
        {
            return await action(progress, _operationCts.Token);
        }
        finally
        {
            OperationPanel.Visibility = Visibility.Collapsed;
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
        => _operationCts?.Cancel();

    private void RecordProgrammaticChange(
        LocalizationEntry entry,
        string before,
        string after,
        string reason)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        EntryHistoryService.Record(entry, before, after, reason);
        _baselineTranslation = after;
        var document = _viewModel?.ActiveDocument;
        if (document is null)
            return;

        ProjectHistoryService.Record(document.FilePath, reason, 1, entry.Key);
        TranslationMemoryService.Invalidate(document);
    }

    private IDisposable SuppressViewModelHistory()
    {
        if (_viewModel is null)
            return EmptyDisposable.Instance;

        var field = typeof(MainViewModel).GetField(
            "_activeSession",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var session = field?.GetValue(_viewModel) as DocumentSession;
        if (session is null)
            return EmptyDisposable.Instance;

        session.HistoryChangeInProgress = true;
        return new ActionDisposable(() => session.HistoryChangeInProgress = false);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
            return;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        CaptureManualHistory();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var dialog = new EntryHistoryWindow(entry) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _baselineTranslation = entry.Translation;
            RefreshDiagnostics();
        }
    }

    private void Consistency_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new ConsistencyWindow(document) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            TranslationMemoryService.Invalidate(document);
            _viewModel.EntriesView.Refresh();
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshDiagnostics();
        }
    }

    private void Glossary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GlossaryWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        GlossaryService.Reload();
        _currentEntry?.RefreshValidation();
        _viewModel?.EntriesView.Refresh();
        RefreshDiagnostics();
    }

    private void Snapshots_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new SnapshotManagerWindow(document) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        if (!dialog.Restored)
            return;

        TranslationMemoryService.Invalidate(document);
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        _viewModel.EntriesView.Refresh();
        RefreshDiagnostics();
    }

    private void CopyOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentEntry?.Original))
            Clipboard.SetText(_currentEntry.Original);
    }

    private async void Monitor_Changed(object? sender, ExternalFileChangedEventArgs e)
    {
        if (_viewModel?.ActiveDocument is not LocalizationDocument document ||
            !string.Equals(
                Path.GetFullPath(document.FilePath),
                Path.GetFullPath(e.Path),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var diff = await ExternalFileDiffService.CompareAsync(document);
                if (diff.Changes.Count == 0 && diff.MissingRows == 0 && diff.AddedRows == 0)
                {
                    _monitor.AcknowledgeCurrent();
                    return;
                }

                _externalDiff = diff;
                ExternalChangeText.Text =
                    $"Файл изменён на диске: переводов отличается {diff.Changes.Count:N0}, новых строк {diff.AddedRows:N0}, отсутствующих {diff.MissingRows:N0}.";
                ExternalChangeBanner.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        });
    }

    private async void ExternalCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_externalDiff is null && _viewModel?.ActiveDocument is LocalizationDocument document)
            _externalDiff = await ExternalFileDiffService.CompareAsync(document);

        if (_externalDiff is not null)
            new ExternalChangeWindow(_externalDiff) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void ExternalReload_Click(object sender, RoutedEventArgs e)
    {
        if (_externalDiff is null || _viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var answer = AppDialog.Show(
            "Загрузить переводы с диска? Текущее состояние сначала будет сохранено в снимок.",
            "Перезагрузка файла",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            Window.GetWindow(this));
        if (answer != MessageBoxResult.Yes)
            return;

        var snapshot = SnapshotService.CreateSnapshot(document, "before-external-reload");
        var changed = ExternalFileDiffService.ApplyDiskTranslations(document, _externalDiff.DiskDocument);
        ProjectHistoryService.Record(
            document.FilePath,
            "Перезагрузка внешней версии",
            changed,
            string.Empty,
            snapshot.Path);
        TranslationMemoryService.Invalidate(document);
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        _viewModel.EntriesView.Refresh();
        _monitor.AcknowledgeCurrent();
        ExternalChangeBanner.Visibility = Visibility.Collapsed;
        _externalDiff = null;
        RefreshDiagnostics();
    }

    private void ExternalOverwrite_Click(object sender, RoutedEventArgs e)
    {
        CommitTranslation();
        if (_viewModel?.SaveCommand.CanExecute(null) == true)
            _viewModel.SaveCommand.Execute(null);
        _monitor.AcknowledgeCurrent();
        ExternalChangeBanner.Visibility = Visibility.Collapsed;
        _externalDiff = null;
    }

    private void ExternalIgnore_Click(object sender, RoutedEventArgs e)
    {
        _monitor.AcknowledgeCurrent();
        ExternalChangeBanner.Visibility = Visibility.Collapsed;
        _externalDiff = null;
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action _action;
        private bool _done;

        public ActionDisposable(Action action) => _action = action;

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            _action();
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

    private enum ChipState
    {
        Neutral,
        Ok,
        Error
    }
}
