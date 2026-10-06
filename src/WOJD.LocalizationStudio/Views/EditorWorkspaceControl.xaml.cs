using System.ComponentModel;
using System.IO;
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
    private readonly ExternalFileMonitorService _monitor = new();
    private CancellationTokenSource? _operationCts;
    private ExternalFileDiffResult? _externalDiff;
    private LocalizationEntry? _currentEntry;
    private string _baselineTranslation = string.Empty;
    private bool _attached;
    private bool _collapsed;
    private double _expandedHeight = 360;
    private LocalizationDocument? _watchedDocument;
    private PaneZoom _paneZoom = PaneZoom.None;

    public event EventHandler? CollapseRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler? FocusModeRequested;

    public EditorWorkspaceControl()
    {
        InitializeComponent();
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
        UpdateCollapseButton();
        UpdateFocusButton();
        ApplyPaneLayout();
        RefreshHeader();
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
        RefreshHeader();
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
        RefreshHeader();
        _viewModel?.EntriesView.Refresh();
        return true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEntry))
        {
            CaptureManualHistory();
            OnSelectionChanged();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.ModifiedCount) or nameof(MainViewModel.TotalCount))
            RefreshHeader();
    }

    private void OnSelectionChanged()
    {
        if (_viewModel is null)
            return;

        _currentEntry = _viewModel.SelectedEntry;
        _baselineTranslation = _currentEntry?.Translation ?? string.Empty;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RefreshHeader();
        RefreshDiagnostics();

        var document = _viewModel.ActiveDocument;
        if (document is not null && !ReferenceEquals(document, _watchedDocument))
        {
            _watchedDocument = document;
            _monitor.Watch(document.FilePath);
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
            _externalDiff = null;
        }
    }

    private void RefreshHeader()
    {
        var document = _viewModel?.ActiveDocument;
        var entry = _viewModel?.SelectedEntry;

        FileNameText.Text = document is null
            ? string.Empty
            : Path.GetFileName(document.FilePath);
        UnsavedIndicator.Visibility = (_viewModel?.ModifiedCount ?? 0) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        EntryMetaText.Text = entry is null
            ? string.Empty
            : $"{entry.Namespace} / {entry.Key}";

        if (document is null || entry is null)
        {
            PositionText.Text = string.Empty;
            return;
        }

        var position = entry.Index > 0 ? entry.Index : 1;
        PositionText.Text = $"{position:N0} / {document.Entries.Count:N0}";
    }

    private void TranslationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_attached)
            return;

        RefreshDiagnostics();
    }

    private void TranslationBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => UpdateTranslationBorder(null);

    private void TranslationBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitCurrentTranslation();
        CaptureManualHistory();
        RefreshHeader();
        RefreshDiagnostics();
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
            ProjectHistoryService.Record(
                document.FilePath,
                "Ручная правка",
                1,
                _currentEntry.Key);
            TranslationMemoryService.Invalidate(document);
        }

        _baselineTranslation = current;
        RefreshHeader();
    }

    private void RefreshDiagnostics()
    {
        var entry = _viewModel?.SelectedEntry;
        if (entry is null)
        {
            HideQaChips();
            TokenPreview.Document = new FlowDocument();
            RestoreStructureButton.Visibility = Visibility.Collapsed;
            AutoCorrectButton.IsEnabled = false;
            AutoCorrectButton.Content = "Автоисправить";
            UpdateTranslationBorder(null);
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            TagsChip.Visibility = Visibility.Collapsed;
            PlaceholdersChip.Visibility = Visibility.Collapsed;
            NewLinesChip.Visibility = Visibility.Collapsed;
            StructureChip.Visibility = Visibility.Visible;
            StructureChipText.Text = "— Нет Original";
            SetChipState(StructureChip, StructureChipText, ChipState.Neutral);
            StructureChip.ToolTip = "Исходный текст отсутствует — структура не проверяется.";
            RestoreStructureButton.Visibility = Visibility.Collapsed;
            RefreshAutoCorrectButton(entry);
            RenderTokenPreview(TranslationBox.Text, null);
            UpdateTranslationBorder(null);
            return;
        }

        var diff = StructuralDiffService.Analyze(entry.Original, TranslationBox.Text);
        var missingTags = diff.MissingTokens.Where(IsTag).ToList();
        var extraTags = diff.ExtraTokens.Where(IsTag).ToList();
        var missingPlaceholders = diff.MissingTokens.Where(x => !IsTag(x)).ToList();
        var extraPlaceholders = diff.ExtraTokens.Where(x => !IsTag(x)).ToList();
        var tagIssue = diff.SourceTags != diff.TargetTags || missingTags.Count > 0 || extraTags.Count > 0 || diff.OrderMismatch;
        var placeholderIssue = diff.SourcePlaceholders != diff.TargetPlaceholders || missingPlaceholders.Count > 0 || extraPlaceholders.Count > 0;
        var newlineIssue = diff.SourceNewLines != diff.TargetNewLines;

        ConfigureQaChip(
            TagsChip,
            TagsChipText,
            "Теги",
            diff.TargetTags,
            diff.SourceTags,
            tagIssue,
            diff.SourceTags > 0 || diff.TargetTags > 0 || tagIssue);
        ConfigureQaChip(
            PlaceholdersChip,
            PlaceholdersChipText,
            "Плейсхолдеры",
            diff.TargetPlaceholders,
            diff.SourcePlaceholders,
            placeholderIssue,
            diff.SourcePlaceholders > 0 || diff.TargetPlaceholders > 0 || placeholderIssue);
        ConfigureQaChip(
            NewLinesChip,
            NewLinesChipText,
            "Переносы",
            diff.TargetNewLines,
            diff.SourceNewLines,
            newlineIssue,
            diff.SourceNewLines > 0 || diff.TargetNewLines > 0 || newlineIssue);

        StructureChip.Visibility = Visibility.Visible;
        StructureChipText.Text = diff.HasIssues ? "Структура ✕" : "Структура ✓";
        SetChipState(
            StructureChip,
            StructureChipText,
            diff.HasIssues ? ChipState.Error : ChipState.Ok);

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

        var restore = StructureProtectionService.RestoreStructure(entry.Original, TranslationBox.Text);
        RestoreStructureButton.Visibility = diff.HasIssues && restore.CanRestore
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshAutoCorrectButton(entry);
        RenderTokenPreview(TranslationBox.Text, diff);
        UpdateTranslationBorder(diff);
    }

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

    private void HideQaChips()
    {
        TagsChip.Visibility = Visibility.Collapsed;
        PlaceholdersChip.Visibility = Visibility.Collapsed;
        NewLinesChip.Visibility = Visibility.Collapsed;
        StructureChip.Visibility = Visibility.Collapsed;
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

    private void UpdateTranslationBorder(StructuralDiffResult? diff)
    {
        if (TranslationBox.IsKeyboardFocusWithin)
        {
            TranslationEditorBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 126, 247));
            return;
        }

        if (string.IsNullOrWhiteSpace(TranslationBox.Text))
        {
            TranslationEditorBorder.BorderBrush = FindResource("BorderBrushApp") as Brush ?? Brushes.LightGray;
            return;
        }

        if (diff?.HasIssues == true)
        {
            TranslationEditorBorder.BorderBrush = Brushes.Firebrick;
            return;
        }

        if (_viewModel?.SelectedEntry?.HasValidationIssues == true)
        {
            TranslationEditorBorder.BorderBrush = Brushes.DarkGoldenrod;
            return;
        }

        TranslationEditorBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 157, 91));
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
                run.Background = segment.IsProblem
                    ? new SolidColorBrush(Color.FromArgb(26, 220, 38, 38))
                    : Brushes.Transparent;
            }
            paragraph.Inlines.Add(run);
        }

        document.Blocks.Add(paragraph);
        TokenPreview.Document = document;
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
        CommitCurrentTranslation();
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
                _owner);
            return;
        }

        entry.Translation = result.Text;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, result.Text, "Восстановление структуры");
        RefreshDiagnostics();
        RefreshHeader();
    }

    private void AutoCorrect_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
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
        RefreshHeader();
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
                    _owner);
                return;
            }

            var preview = new TranslationCorrectionPreviewWindow(entries.Count, plan)
            {
                Owner = _owner,
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
            RefreshDiagnostics();
            RefreshHeader();
            _viewModel?.EntriesView.Refresh();
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
                _owner);
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
            RefreshHeader();
        }
    }

    private void Consistency_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new ConsistencyWindow(document)
        {
            Owner = _owner,
            Title = "Согласованность переводов"
        };
        if (dialog.ShowDialog() == true)
        {
            TranslationMemoryService.Invalidate(document);
            _viewModel.EntriesView.Refresh();
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            RefreshDiagnostics();
            RefreshHeader();
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
            return;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void CopyOriginal_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_viewModel?.SelectedEntry?.Original ?? string.Empty);

    private void CopyTranslation_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(TranslationBox.Text);

    private void CopyKey_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(_viewModel?.SelectedEntry?.Key ?? string.Empty);

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private void ClearTranslation_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;

        var before = entry.Translation;
        if (before.Length == 0)
            return;
        entry.Translation = string.Empty;
        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        RecordProgrammaticChange(entry, before, string.Empty, "Очистка перевода");
        RefreshDiagnostics();
        RefreshHeader();
    }

    private async void ExportCurrent_Click(object sender, RoutedEventArgs e)
    {
        CommitCurrentTranslation();
        if (_viewModel?.SelectedEntry is not LocalizationEntry entry)
            return;
        await _viewModel.ExportEntriesAsync([entry], "selected");
    }

    private void HorizontalLayout_Click(object sender, RoutedEventArgs e)
    {
        var settings = EditorSettingsService.Current;
        settings.EditorPaneLayout = "Horizontal";
        settings.TranslationOnlyMode = false;
        EditorSettingsService.Save(settings);
        _paneZoom = PaneZoom.None;
        ApplyPaneLayout();
    }

    private void VerticalLayout_Click(object sender, RoutedEventArgs e)
    {
        var settings = EditorSettingsService.Current;
        settings.EditorPaneLayout = "Vertical";
        settings.TranslationOnlyMode = false;
        EditorSettingsService.Save(settings);
        _paneZoom = PaneZoom.None;
        ApplyPaneLayout();
    }

    private void TranslationOnly_Click(object sender, RoutedEventArgs e)
    {
        var settings = EditorSettingsService.Current;
        settings.TranslationOnlyMode = !settings.TranslationOnlyMode;
        EditorSettingsService.Save(settings);
        _paneZoom = PaneZoom.None;
        ApplyPaneLayout();
    }

    private void OriginalHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;
        _paneZoom = _paneZoom == PaneZoom.Original ? PaneZoom.None : PaneZoom.Original;
        ApplyPaneLayout();
        e.Handled = true;
    }

    private void TranslationHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;
        _paneZoom = _paneZoom == PaneZoom.Translation ? PaneZoom.None : PaneZoom.Translation;
        ApplyPaneLayout();
        e.Handled = true;
    }

    private void ApplyPaneLayout()
    {
        var settings = EditorSettingsService.Current;
        var translationOnly = settings.TranslationOnlyMode || _paneZoom == PaneZoom.Translation;
        var originalOnly = _paneZoom == PaneZoom.Original;
        var vertical = string.Equals(settings.EditorPaneLayout, "Vertical", StringComparison.OrdinalIgnoreCase);

        Grid.SetColumnSpan(OriginalPane, 1);
        Grid.SetRowSpan(OriginalPane, 1);
        Grid.SetColumnSpan(TranslationPane, 1);
        Grid.SetRowSpan(TranslationPane, 1);

        if (translationOnly)
        {
            PaneColumn1.Width = new GridLength(1, GridUnitType.Star);
            PaneColumnDivider.Width = new GridLength(0);
            PaneColumn2.Width = new GridLength(0);
            PaneRow1.Height = new GridLength(1, GridUnitType.Star);
            PaneRowDivider.Height = new GridLength(0);
            PaneRow2.Height = new GridLength(0);
            OriginalPane.Visibility = Visibility.Collapsed;
            PaneDivider.Visibility = Visibility.Collapsed;
            TranslationPane.Visibility = Visibility.Visible;
            TranslationPane.Margin = new Thickness(0);
            Grid.SetColumn(TranslationPane, 0);
            Grid.SetRow(TranslationPane, 0);
            return;
        }

        if (originalOnly)
        {
            PaneColumn1.Width = new GridLength(1, GridUnitType.Star);
            PaneColumnDivider.Width = new GridLength(0);
            PaneColumn2.Width = new GridLength(0);
            PaneRow1.Height = new GridLength(1, GridUnitType.Star);
            PaneRowDivider.Height = new GridLength(0);
            PaneRow2.Height = new GridLength(0);
            OriginalPane.Visibility = Visibility.Visible;
            PaneDivider.Visibility = Visibility.Collapsed;
            TranslationPane.Visibility = Visibility.Collapsed;
            OriginalPane.Margin = new Thickness(0);
            Grid.SetColumn(OriginalPane, 0);
            Grid.SetRow(OriginalPane, 0);
            return;
        }

        OriginalPane.Visibility = Visibility.Visible;
        TranslationPane.Visibility = Visibility.Visible;
        PaneDivider.Visibility = Visibility.Visible;

        if (!vertical)
        {
            PaneColumn1.Width = new GridLength(1, GridUnitType.Star);
            PaneColumnDivider.Width = new GridLength(1);
            PaneColumn2.Width = new GridLength(1, GridUnitType.Star);
            PaneRow1.Height = new GridLength(1, GridUnitType.Star);
            PaneRowDivider.Height = new GridLength(0);
            PaneRow2.Height = new GridLength(0);

            Grid.SetRow(OriginalPane, 0);
            Grid.SetColumn(OriginalPane, 0);
            OriginalPane.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetRow(PaneDivider, 0);
            Grid.SetColumn(PaneDivider, 1);
            Grid.SetRow(TranslationPane, 0);
            Grid.SetColumn(TranslationPane, 2);
            TranslationPane.Margin = new Thickness(12, 0, 0, 0);
        }
        else
        {
            PaneColumn1.Width = new GridLength(1, GridUnitType.Star);
            PaneColumnDivider.Width = new GridLength(0);
            PaneColumn2.Width = new GridLength(0);
            PaneRow1.Height = new GridLength(1, GridUnitType.Star);
            PaneRowDivider.Height = new GridLength(1);
            PaneRow2.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(OriginalPane, 0);
            Grid.SetColumn(OriginalPane, 0);
            OriginalPane.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(PaneDivider, 1);
            Grid.SetColumn(PaneDivider, 0);
            Grid.SetRow(TranslationPane, 2);
            Grid.SetColumn(TranslationPane, 0);
            TranslationPane.Margin = new Thickness(0, 12, 0, 0);
        }
    }

    private void FocusMode_Click(object sender, RoutedEventArgs e)
        => FocusModeRequested?.Invoke(this, EventArgs.Empty);

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        var settings = EditorSettingsService.Current;
        settings.EditorCollapsed = _collapsed;
        settings.EditorHeight = _expandedHeight;
        EditorSettingsService.Save(settings);
        UpdateCollapseButton();
        CollapseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCollapseButton()
    {
        CollapseButton.Content = _collapsed ? "⌄" : "⌃";
        CollapseButton.ToolTip = _collapsed
            ? "Развернуть редактор"
            : "Свернуть редактор";
    }

    private void UpdateFocusButton()
    {
        FocusModeButton.Content = EditorSettingsService.Current.FocusMode ? "⤢" : "⛶";
        FocusModeButton.ToolTip = EditorSettingsService.Current.FocusMode
            ? "Выйти из режима фокус"
            : "Режим фокус";
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
        if (_externalDiff is null &&
            _viewModel?.ActiveDocument is LocalizationDocument document)
        {
            _externalDiff = await ExternalFileDiffService.CompareAsync(document);
        }

        if (_externalDiff is not null)
            new ExternalChangeWindow(_externalDiff) { Owner = _owner }.ShowDialog();
    }

    private void ExternalReload_Click(object sender, RoutedEventArgs e)
    {
        if (_externalDiff is null ||
            _viewModel?.ActiveDocument is not LocalizationDocument document)
        {
            return;
        }

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
        RefreshHeader();
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

    private enum PaneZoom
    {
        None,
        Original,
        Translation
    }
}
