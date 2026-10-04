using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class EditorWorkspaceControl : UserControl
{
    private MainWindow? _owner;
    private MainViewModel? _viewModel;
    private DataGrid? _entriesGrid;
    private readonly ExternalFileMonitorService _monitor = new();
    private CancellationTokenSource? _operationCts;
    private ExternalFileDiffResult? _externalDiff;
    private LocalizationEntry? _currentEntry;
    private string _baselineTranslation = string.Empty;
    private bool _updatingReviewStatus;
    private bool _attached;
    private bool _collapsed;
    private double _expandedHeight = 360;

    public event EventHandler? CollapseRequested;
    public event EventHandler? SettingsChanged;

    public EditorWorkspaceControl()
    {
        InitializeComponent();
        ReviewStatusCombo.Items.Add(new ReviewOption("Не проверено", ReviewState.Unreviewed));
        ReviewStatusCombo.Items.Add(new ReviewOption("Проверено", ReviewState.Reviewed));
        ReviewStatusCombo.Items.Add(new ReviewOption("Требует правки", ReviewState.NeedsFix));
        ReviewStatusCombo.Items.Add(new ReviewOption("Пропустить", ReviewState.Skipped));
        ReviewStatusCombo.SelectedIndex = 0;
        _monitor.Changed += Monitor_Changed;
        Unloaded += (_, _) => _operationCts?.Cancel();
    }

    public void Attach(
        MainWindow owner,
        MainViewModel viewModel,
        DataGrid entriesGrid)
    {
        if (_attached)
            return;

        _attached = true;
        _owner = owner;
        _viewModel = viewModel;
        _entriesGrid = entriesGrid;
        DataContext = viewModel;

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        owner.Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            CaptureManualHistory();
            _monitor.Dispose();
        };

        DataObject.AddPastingHandler(TranslationBox, TranslationBox_Pasting);
        ApplySettings();
        OnSelectionChanged();
    }

    public void ApplySettings()
    {
        var settings = EditorSettingsService.Current;
        _expandedHeight = settings.EditorHeight;
        TranslationBox.FontSize = settings.EditorFontSize;
        TranslationBox.TextWrapping = settings.WrapTranslation
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
        TokenPreviewBorder.Visibility = settings.ShowTokenPreview
            ? Visibility.Visible
            : Visibility.Collapsed;
        QaDetailsBorder.Visibility = settings.ShowQaDetails
            ? Visibility.Visible
            : Visibility.Collapsed;
        _collapsed = settings.EditorCollapsed;
        CollapseButton.Content = _collapsed ? "Развернуть" : "Свернуть";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsCollapsed => _collapsed;
    public double DesiredEditorHeight => _collapsed ? 54 : _expandedHeight;

    public void SetExpandedHeight(double value)
    {
        if (_collapsed)
            return;

        _expandedHeight = Math.Clamp(value, 180, 760);
        var settings = EditorSettingsService.Current;
        settings.EditorHeight = _expandedHeight;
        EditorSettingsService.Save(settings);
    }

    public void CommitTranslation() => CommitCurrentTranslation();

    public bool TryBulkUndo()
    {
        CommitCurrentTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null || !BulkUndoService.TryUndo(document.FilePath))
            return false;

        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RefreshDiagnostics();
        _viewModel?.EntriesView.Refresh();
        return true;
    }

    public bool TryBulkRedo()
    {
        CommitCurrentTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null || !BulkUndoService.TryRedo(document.FilePath))
            return false;

        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RefreshDiagnostics();
        _viewModel?.EntriesView.Refresh();
        return true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEntry))
        {
            CaptureManualHistory();
            OnSelectionChanged();
        }
    }

    private void OnSelectionChanged()
    {
        if (_viewModel is null)
            return;

        _currentEntry = _viewModel.SelectedEntry;
        _baselineTranslation = _currentEntry?.Translation ?? string.Empty;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RefreshDiagnostics();
        RefreshReviewStatus();
        RefreshGlossaryMatches();
        _ = RefreshTranslationMemoryAsync(_currentEntry);

        var document = _viewModel.ActiveDocument;
        if (document is not null && !ReferenceEquals(document, _watchedDocument))
        {
            _watchedDocument = document;
            _monitor.Watch(document.FilePath);
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
            _externalDiff = null;
        }
    }

    private LocalizationDocument? _watchedDocument;

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_attached)
            return;
        RefreshDiagnostics();
    }

    private void TranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitCurrentTranslation();
        CaptureManualHistory();
    }

    private void CommitCurrentTranslation()
    {
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void CaptureManualHistory()
    {
        if (_viewModel is null || _currentEntry is null)
            return;

        CommitCurrentTranslation();
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
            ReviewWorkflowService.OnTranslationChanged(document.FilePath, _currentEntry);
            ProjectHistoryService.Record(
                document.FilePath,
                "Ручная правка",
                1,
                _currentEntry.Key);
            TranslationMemoryService.Invalidate(document);
        }

        _baselineTranslation = current;
        RefreshReviewStatus();
    }

    private void RefreshDiagnostics()
    {
        var entry = _viewModel?.SelectedEntry;
        if (entry is null)
        {
            StructureSummaryText.Text = string.Empty;
            StructureDetailsText.Text = string.Empty;
            TokenPreview.Document = new FlowDocument();
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            StructureSummaryText.Text = "— Исходный текст отсутствует — структура не проверяется";
            StructureSummaryText.Foreground = Brushes.DimGray;
            StructureDetailsText.Text = string.Empty;
            RenderTokenPreview(entry.Translation, null);
            return;
        }

        var diff = StructuralDiffService.Analyze(entry.Original, TranslationBox.Text);
        var ok = !diff.HasIssues;
        StructureSummaryText.Text =
            $"Теги {diff.TargetTags}/{diff.SourceTags} {(diff.SourceTags == diff.TargetTags && !diff.MissingTokens.Any(IsTag) && !diff.ExtraTokens.Any(IsTag) && !diff.OrderMismatch ? "✓" : "✕")}   " +
            $"Плейсхолдеры {diff.TargetPlaceholders}/{diff.SourcePlaceholders} {(diff.SourcePlaceholders == diff.TargetPlaceholders && !diff.MissingTokens.Any(x => !IsTag(x)) && !diff.ExtraTokens.Any(x => !IsTag(x)) ? "✓" : "✕")}   " +
            $"Переносы {diff.TargetNewLines}/{diff.SourceNewLines} {(diff.SourceNewLines == diff.TargetNewLines ? "✓" : "✕")}";
        StructureSummaryText.Foreground = ok ? Brushes.SeaGreen : Brushes.Firebrick;

        var details = new List<string>();
        if (diff.MissingTokens.Count > 0)
            details.Add("нет: " + string.Join(", ", diff.MissingTokens.Distinct()));
        if (diff.ExtraTokens.Count > 0)
            details.Add("лишнее: " + string.Join(", ", diff.ExtraTokens.Distinct()));
        if (diff.OrderMismatch)
            details.Add("нарушен порядок тегов");
        if (diff.SourceNewLines != diff.TargetNewLines)
            details.Add($"переносы {diff.SourceNewLines} → {diff.TargetNewLines}");
        StructureDetailsText.Text = details.Count == 0 ? "Структура совпадает с Original." : string.Join(" · ", details);
        StructureDetailsText.ToolTip = StructureDetailsText.Text;
        RenderTokenPreview(TranslationBox.Text, diff);
    }

    private static bool IsTag(string token) => token.StartsWith('<');

    private void RenderTokenPreview(string text, StructuralDiffResult? diff)
    {
        if (EditorSettingsService.Current.ShowTokenPreview == false)
            return;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = TranslationBox.FontFamily,
            FontSize = Math.Max(11, TranslationBox.FontSize - 1)
        };
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        foreach (var segment in StructuralDiffService.TokenizeForPreview(text, diff, true))
        {
            var run = new Run(segment.Text);
            if (segment.IsToken)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.Foreground = segment.IsProblem ? Brushes.Firebrick : Brushes.RoyalBlue;
                run.Background = segment.IsProblem ? new SolidColorBrush(Color.FromArgb(26, 220, 38, 38)) : Brushes.Transparent;
            }
            paragraph.Inlines.Add(run);
        }

        document.Blocks.Add(paragraph);
        TokenPreview.Document = document;
    }

    private void TranslationBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ProtectStructureCheckBox.IsChecked != true ||
            _viewModel?.SelectedEntry is not LocalizationEntry entry ||
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
            !StructureProtectionService.WouldWorsenStructure(
                entry.Original,
                TranslationBox.Text,
                proposed))
        {
            return;
        }

        System.Media.SystemSounds.Beep.Play();
        e.Handled = true;
        StructureDetailsText.Text = "Защита остановила действие: оно повреждало тег или плейсхолдер.";
    }

    private void TranslationBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (ProtectStructureCheckBox.IsChecked != true ||
            _viewModel?.SelectedEntry is not LocalizationEntry entry ||
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
            _owner);
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
        var index = box.SelectionStart - 1;
        return box.Text.Remove(index, 1);
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
        CommitCurrentTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var before = entry.Translation;
        var result = StructureProtectionService.RestoreStructure(entry.Original, before);
        if (!result.Changed)
        {
            AppDialog.Show(result.Message, "Восстановление структуры", MessageBoxButton.OK,
                result.CanRestore ? MessageBoxImage.Information : MessageBoxImage.Warning, _owner);
            return;
        }

        entry.Translation = result.Text;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, result.Text, "Восстановление структуры");
        RefreshDiagnostics();
    }

    private void AutoCorrect_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var before = entry.Translation;
        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
        if (!result.HasChanges)
        {
            AppDialog.Show(
                result.RequiresReview ? result.ReviewReason : "Безопасных автоисправлений не найдено.",
                "Автоисправление",
                MessageBoxButton.OK,
                result.RequiresReview ? MessageBoxImage.Warning : MessageBoxImage.Information,
                _owner);
            return;
        }

        entry.Translation = result.CorrectedText;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, result.CorrectedText, "Автоисправление");
        RefreshDiagnostics();
    }

    private async void BulkAutoCorrect_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null)
            return;

        try
        {
            var entries = document.Entries.ToList();
            var plan = await RunOperationAsync(
                "Анализ автоисправлений",
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
                    $"Проверено строк: {entries.Count:N0}. Изменений нет. Требуют ручной проверки: {plan.ReviewItems.Count:N0}.",
                    "Массовое автоисправление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    _owner);
                return;
            }

            var preview = new TranslationCorrectionPreviewWindow(entries.Count, plan) { Owner = _owner };
            if (preview.ShowDialog() != true || preview.SelectedChanges.Count == 0)
                return;

            var snapshot = SnapshotService.CreateSnapshot(document, "before-bulk-autocorrect");
            var selected = preview.SelectedChanges.ToList();
            var changes = selected.Select(x => new BulkTextChange(x.Entry, x.Before, x.After)).ToList();
            TranslationCorrectionApplyResult applyResult;

            using (SuppressViewModelHistory())
                applyResult = plan.Apply(selected);

            BulkUndoService.Push(document.FilePath, "Массовое автоисправление", changes);
            ProjectHistoryService.Record(
                document.FilePath,
                "Массовое автоисправление",
                applyResult.AppliedEntries,
                $"Исправлений: {selected.Sum(x => x.FixCount):N0}; ручная проверка: {plan.ReviewItems.Count:N0}",
                snapshot.Path);
            TranslationMemoryService.Invalidate(document);
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshDiagnostics();
            _viewModel?.EntriesView.Refresh();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, "Ошибка массового автоисправления", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
        }
    }

    private async void AiCurrent_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;
        if (!EnsureAiConfigured())
            return;

        try
        {
            var result = await RunOperationAsync(
                "ИИ-проверка строки",
                async (_, token) => await AiCorrectionService.ReviewAsync(entry, token));
            if (result is null)
                return;

            var preview = new AiReviewPreviewWindow(entry, result) { Owner = _owner };
            if (preview.ShowDialog() == true && preview.ApplySuggestion && result.Changed)
            {
                var before = entry.Translation;
                entry.Translation = result.Translation;
                TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                RecordProgrammaticChange(entry, before, result.Translation, "ИИ-проверка");
                RefreshDiagnostics();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, "Ошибка ИИ-проверки", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
        }
    }

    private async void AiBatch_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        var document = _viewModel?.ActiveDocument;
        if (document is null || !EnsureAiConfigured())
            return;

        var candidates = GetAiCandidates(document);
        if (candidates.Count == 0)
        {
            AppDialog.Show("Нет подходящих строк для пакетной ИИ-проверки.", "ИИ-проверка", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
            return;
        }

        var settings = EditorSettingsService.Current;
        var selected = candidates.Take(settings.AiBatchLimit).ToList();
        var estimate = AiBatchReviewService.Estimate(selected, settings);
        var answer = AppDialog.Show(
            $"Будет проверено строк: {selected.Count:N0} из {candidates.Count:N0}.\n" +
            $"Оценка токенов: input ≈ {estimate.ApproxInputTokens:N0}, output ≈ {estimate.ApproxOutputTokens:N0}.\n" +
            $"Ориентировочная стоимость: {estimate.CostText}.\n\nЗапустить?",
            "Пакетная ИИ-проверка",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            _owner);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var results = await RunOperationAsync(
                "ИИ-проверка",
                (progress, token) => AiBatchReviewService.ReviewAsync(selected, settings, progress, token));
            if (results is null)
                return;

            var suggestions = results
                .Where(x => x.Success && x.Review is { Changed: true })
                .Select(x => new AiBatchSuggestion
                {
                    Entry = x.Entry,
                    Before = x.Entry.Translation,
                    After = x.Review!.Translation,
                    Reason = x.Review.Reason
                })
                .ToList();
            var failures = results.Count(x => !x.Success);

            if (suggestions.Count == 0)
            {
                AppDialog.Show(
                    $"ИИ не предложил изменений. Ошибок запросов: {failures:N0}.",
                    "ИИ-проверка",
                    MessageBoxButton.OK,
                    failures == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning,
                    _owner);
                return;
            }

            var preview = new AiBatchPreviewWindow(suggestions) { Owner = _owner };
            if (preview.ShowDialog() != true || preview.SelectedSuggestions.Count == 0)
                return;

            var chosen = preview.SelectedSuggestions.ToList();
            var snapshot = SnapshotService.CreateSnapshot(document, "before-ai-batch");
            var bulk = chosen.Select(x => new BulkTextChange(x.Entry, x.Before, x.After)).ToList();
            using (SuppressViewModelHistory())
            {
                foreach (var item in chosen)
                {
                    if (string.Equals(item.Entry.Translation, item.Before, StringComparison.Ordinal))
                        item.Entry.Translation = item.After;
                }
            }
            BulkUndoService.Push(document.FilePath, "Пакетная ИИ-проверка", bulk);
            ProjectHistoryService.Record(
                document.FilePath,
                "Пакетная ИИ-проверка",
                chosen.Count,
                $"Модель: {AiCorrectionService.Model}; ошибок API: {failures:N0}",
                snapshot.Path);
            TranslationMemoryService.Invalidate(document);
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshDiagnostics();
            _viewModel?.EntriesView.Refresh();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, "Ошибка пакетной ИИ-проверки", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
        }
    }

    private List<LocalizationEntry> GetAiCandidates(LocalizationDocument document)
    {
        var selectedRows = _entriesGrid?.SelectedItems.OfType<LocalizationEntry>().ToList() ?? [];
        if (selectedRows.Count > 1)
            return selectedRows.Where(x => !string.IsNullOrWhiteSpace(x.Original) && !string.IsNullOrWhiteSpace(x.Translation)).ToList();

        return document.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Original) &&
                        !string.IsNullOrWhiteSpace(x.Translation) &&
                        (x.HasValidationIssues ||
                         ReviewWorkflowService.GetStatus(document.FilePath, x) == ReviewState.NeedsFix ||
                         x.HasSuspiciousLengthIssue))
            .ToList();
    }

    private bool EnsureAiConfigured()
    {
        if (AiCorrectionService.IsConfigured)
            return true;

        var dialog = new AiSettingsWindow { Owner = _owner };
        if (dialog.ShowDialog() != true)
            return false;

        AiCorrectionService.ConfigureSession(dialog.ApiKey, dialog.Model);
        return AiCorrectionService.IsConfigured;
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

        ReviewWorkflowService.OnTranslationChanged(document.FilePath, entry);
        ProjectHistoryService.Record(document.FilePath, reason, 1, entry.Key);
        TranslationMemoryService.Invalidate(document);
        RefreshReviewStatus();
    }

    private IDisposable SuppressViewModelHistory()
    {
        if (_viewModel is null)
            return EmptyDisposable.Instance;

        var field = typeof(MainViewModel).GetField("_activeSession", BindingFlags.Instance | BindingFlags.NonPublic);
        var session = field?.GetValue(_viewModel) as DocumentSession;
        if (session is null)
            return EmptyDisposable.Instance;

        session.HistoryChangeInProgress = true;
        return new ActionDisposable(() => session.HistoryChangeInProgress = false);
    }

    private async Task RefreshTranslationMemoryAsync(LocalizationEntry? entry)
    {
        if (entry is null || _viewModel?.ActiveDocument is not LocalizationDocument document)
        {
            TmList.ItemsSource = null;
            return;
        }

        var expected = entry;
        try
        {
            var index = await TranslationMemoryService.GetOrBuildAsync(document);
            if (!ReferenceEquals(_viewModel?.SelectedEntry, expected))
                return;

            TmList.ItemsSource = index.Search(expected)
                .Select(x => new TmDisplayItem(x))
                .ToList();
        }
        catch
        {
            TmList.ItemsSource = null;
        }
    }

    private void RefreshGlossaryMatches()
    {
        var entry = _viewModel?.SelectedEntry;
        GlossaryMatchesList.ItemsSource = entry is null
            ? null
            : GlossaryService.Match(entry.Original, entry.Translation)
                .Select(x => new GlossaryDisplayItem(x))
                .ToList();
    }

    private void ApplyTm_Click(object sender, RoutedEventArgs e) => ApplySelectedTm();
    private void TmList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ApplySelectedTm();

    private void ApplySelectedTm()
    {
        if (TmList.SelectedItem is not TmDisplayItem item ||
            _viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        CommitCurrentTranslation();
        var before = entry.Translation;
        entry.Translation = item.Suggestion.Translation;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, entry.Translation, "Translation Memory");
        RefreshDiagnostics();
        RefreshGlossaryMatches();
    }

    private void RefreshReviewStatus()
    {
        if (_viewModel?.ActiveDocument is not LocalizationDocument document ||
            _viewModel.SelectedEntry is not LocalizationEntry entry)
        {
            return;
        }

        var state = ReviewWorkflowService.GetStatus(document.FilePath, entry);
        _updatingReviewStatus = true;
        try
        {
            ReviewStatusCombo.SelectedItem = ReviewStatusCombo.Items
                .OfType<ReviewOption>()
                .FirstOrDefault(x => x.State == state);
        }
        finally
        {
            _updatingReviewStatus = false;
        }
    }

    private void ReviewStatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReviewStatus ||
            ReviewStatusCombo.SelectedItem is not ReviewOption option ||
            _viewModel?.ActiveDocument is not LocalizationDocument document ||
            _viewModel.SelectedEntry is not LocalizationEntry entry)
            return;

        CommitCurrentTranslation();
        ReviewWorkflowService.SetStatus(document.FilePath, entry, option.State);
    }

    private void ReviewedNext_Click(object sender, RoutedEventArgs e)
    {
        SetStatusAndNext(ReviewState.Reviewed);
    }

    private void NeedsFixNext_Click(object sender, RoutedEventArgs e)
    {
        SetStatusAndNext(ReviewState.NeedsFix);
    }

    private void SetStatusAndNext(ReviewState state)
    {
        CommitCurrentTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document ||
            _viewModel.SelectedEntry is not LocalizationEntry entry)
            return;

        ReviewWorkflowService.SetStatus(document.FilePath, entry, state);
        NavigateNextUnreviewed();
    }

    private void NavigateNextUnreviewed()
    {
        if (_viewModel?.ActiveDocument is not LocalizationDocument document || document.Entries.Count == 0)
            return;

        var start = _viewModel.SelectedEntry is null ? -1 : document.Entries.IndexOf(_viewModel.SelectedEntry);
        for (var offset = 1; offset <= document.Entries.Count; offset++)
        {
            var candidate = document.Entries[(start + offset) % document.Entries.Count];
            if (ReviewWorkflowService.GetStatus(document.FilePath, candidate) != ReviewState.Unreviewed)
                continue;

            _viewModel.SelectedEntry = candidate;
            _viewModel.EntriesView.MoveCurrentTo(candidate);
            if (_entriesGrid is not null)
            {
                _entriesGrid.SelectedItem = candidate;
                _entriesGrid.ScrollIntoView(candidate);
            }
            TranslationBox.Focus();
            return;
        }

        AppDialog.Show("Непроверенных строк больше нет.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        CaptureManualHistory();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var dialog = new EntryHistoryWindow(entry) { Owner = _owner };
        if (dialog.ShowDialog() == true)
        {
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _baselineTranslation = entry.Translation;
            RefreshDiagnostics();
        }
    }

    private void Snapshots_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new SnapshotManagerWindow(document) { Owner = _owner };
        if (dialog.ShowDialog() == true && dialog.Restored)
        {
            TranslationMemoryService.Invalidate(document);
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _viewModel.EntriesView.Refresh();
            RefreshDiagnostics();
        }
    }

    private void Consistency_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new ConsistencyWindow(document) { Owner = _owner };
        if (dialog.ShowDialog() == true)
        {
            TranslationMemoryService.Invalidate(document);
            _viewModel.EntriesView.Refresh();
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }
    }

    private void Glossary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GlossaryWindow { Owner = _owner };
        if (dialog.ShowDialog() != true)
            return;

        GlossaryService.Reload();
        _viewModel?.SelectedEntry?.RefreshValidation();
        RefreshGlossaryMatches();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditorSettingsWindow { Owner = _owner };
        if (dialog.ShowDialog() == true)
            ApplySettings();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        var settings = EditorSettingsService.Current;
        settings.EditorCollapsed = _collapsed;
        settings.EditorHeight = _expandedHeight;
        EditorSettingsService.Save(settings);
        CollapseButton.Content = _collapsed ? "Развернуть" : "Свернуть";
        CollapseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void Monitor_Changed(object? sender, ExternalFileChangedEventArgs e)
    {
        if (_viewModel?.ActiveDocument is not LocalizationDocument document ||
            !string.Equals(Path.GetFullPath(document.FilePath), Path.GetFullPath(e.Path), StringComparison.OrdinalIgnoreCase))
            return;

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
                ExternalChangeText.Text = $"Файл изменён на диске: переводов отличается {diff.Changes.Count:N0}, новых строк {diff.AddedRows:N0}, отсутствующих {diff.MissingRows:N0}.";
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
            new ExternalChangeWindow(_externalDiff) { Owner = _owner }.ShowDialog();
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
            _owner);
        if (answer != MessageBoxResult.Yes)
            return;

        var snapshot = SnapshotService.CreateSnapshot(document, "before-external-reload");
        var changed = ExternalFileDiffService.ApplyDiskTranslations(document, _externalDiff.DiskDocument);
        ProjectHistoryService.Record(document.FilePath, "Перезагрузка внешней версии", changed, string.Empty, snapshot.Path);
        TranslationMemoryService.Invalidate(document);
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        _viewModel.EntriesView.Refresh();
        _monitor.AcknowledgeCurrent();
        ExternalChangeBanner.Visibility = Visibility.Collapsed;
        _externalDiff = null;
    }

    private void ExternalOverwrite_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
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

    private sealed record ReviewOption(string Text, ReviewState State)
    {
        public override string ToString() => Text;
    }

    private sealed record TmDisplayItem(TranslationMemorySuggestion Suggestion)
    {
        public string Display => $"{Suggestion.Score}%  {Suggestion.Translation}  · {Suggestion.Key}";
    }

    private sealed record GlossaryDisplayItem(GlossaryMatch Match)
    {
        public string Display => $"{(Match.IsSatisfied ? "✓" : Match.Required ? "⚠" : "•")} {Match.Source} → {Match.Target}";
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action _action;
        private bool _done;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _action();
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
