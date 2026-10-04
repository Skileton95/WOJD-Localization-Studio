using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class StaticEditorEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, State> Installed = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || Installed.TryGetValue(window, out _))
            return;

        var state = new State();
        Installed.Add(window, state);

        window.Dispatcher.BeginInvoke(
            () => Install(window, state),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(MainWindow window, State state)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox ||
            window.FindName("EntriesGrid") is not DataGrid entriesGrid ||
            window.FindName("AutoCorrectButton") is not Button autoButton ||
            window.FindName("BulkAutoCorrectButton") is not Button bulkButton ||
            window.FindName("AiReviewButton") is not Button aiButton ||
            window.FindName("StructureQaBorder") is not Border qaBorder ||
            window.FindName("StructureQaText") is not TextBlock qaText ||
            window.FindName("PrevStructureButton") is not Button previousStructure ||
            window.FindName("NextStructureButton") is not Button nextStructure ||
            window.FindName("RestoreStructureButton") is not Button restoreStructure ||
            window.FindName("ProtectionToggle") is not CheckBox protectionToggle ||
            window.FindName("ReviewFilterCombo") is not ComboBox reviewFilter ||
            window.FindName("ReviewStatusCombo") is not ComboBox reviewStatus ||
            window.FindName("QaTagsFilterButton") is not Button tagsFilter ||
            window.FindName("QaPlaceholdersFilterButton") is not Button placeholdersFilter ||
            window.FindName("QaNewLinesFilterButton") is not Button newLinesFilter)
        {
            return;
        }

        state.ViewModel = viewModel;
        state.TranslationBox = translationBox;
        state.EntriesGrid = entriesGrid;
        state.QaBorder = qaBorder;
        state.QaText = qaText;
        state.ProtectionToggle = protectionToggle;

        InstallSafeTranslationBinding(window, viewModel, translationBox, state);
        InstallCombinedFilters(window, viewModel, state, tagsFilter, placeholdersFilter, newLinesFilter, reviewFilter);
        InstallReviewWorkflow(window, viewModel, entriesGrid, translationBox, reviewStatus, state);
        InstallStructureQa(window, viewModel, entriesGrid, translationBox, qaBorder, qaText, previousStructure, nextStructure, restoreStructure, state);
        InstallProtection(window, viewModel, translationBox, protectionToggle, state);
        InstallAutoCorrection(window, viewModel, translationBox, autoButton, bulkButton, state);
        InstallAi(window, viewModel, entriesGrid, translationBox, aiButton, state);
        InstallBulkUndo(window, viewModel, translationBox, state);

        RefreshStructureQa(viewModel, translationBox, qaBorder, qaText);
        RefreshReviewStatus(viewModel, reviewStatus, state);
    }

    private static void InstallSafeTranslationBinding(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        var binding = new Binding("SelectedEntry.Translation")
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit
        };
        BindingOperations.SetBinding(translationBox, TextBox.TextProperty, binding);

        void Commit() => CommitTranslation(viewModel, translationBox, state, "Ручная правка");

        translationBox.LostKeyboardFocus += (_, _) => Commit();
        window.Deactivated += (_, _) => Commit();
        window.Closing += (_, _) => Commit();

        window.PreviewMouseDown += (_, args) =>
        {
            if (!translationBox.IsKeyboardFocusWithin ||
                args.OriginalSource is not DependencyObject source ||
                IsInside(source, translationBox))
            {
                return;
            }

            Commit();
        };

        PropertyChangedEventHandler selectedHandler = (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.SelectedEntry))
                return;

            state.TranslationBaseline = viewModel.SelectedEntry?.Translation ?? string.Empty;
            window.Dispatcher.BeginInvoke(
                () =>
                {
                    translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                    if (state.QaBorder is not null && state.QaText is not null)
                        RefreshStructureQa(viewModel, translationBox, state.QaBorder, state.QaText);
                    if (state.ReviewStatusCombo is not null)
                        RefreshReviewStatus(viewModel, state.ReviewStatusCombo, state);
                },
                DispatcherPriority.Background);
        };

        viewModel.PropertyChanged += selectedHandler;
        window.Closed += (_, _) => viewModel.PropertyChanged -= selectedHandler;
    }

    private static void InstallCombinedFilters(
        MainWindow window,
        MainViewModel viewModel,
        State state,
        Button tags,
        Button placeholders,
        Button newLines,
        ComboBox reviewFilter)
    {
        state.BaseFilter = viewModel.EntriesView.Filter;
        viewModel.EntriesView.Filter = item =>
            (state.BaseFilter?.Invoke(item) ?? true) &&
            MatchesQaFilter(item, state.QaFilter) &&
            MatchesReviewFilter(viewModel, item, state.ReviewFilter);

        tags.ToolTip = "Только строки с ошибками тегов <…>";
        placeholders.ToolTip = "Только строки с ошибками плейсхолдеров";
        newLines.ToolTip = "Только строки с ошибками переносов";

        tags.Click += (_, _) => ActivateQaFilter(viewModel, state, QaFilter.Tags);
        placeholders.Click += (_, _) => ActivateQaFilter(viewModel, state, QaFilter.Placeholders);
        newLines.Click += (_, _) => ActivateQaFilter(viewModel, state, QaFilter.NewLines);

        state.QaButtons[QaFilter.Tags] = tags;
        state.QaButtons[QaFilter.Placeholders] = placeholders;
        state.QaButtons[QaFilter.NewLines] = newLines;

        reviewFilter.Items.Clear();
        reviewFilter.Items.Add(new ReviewFilterOption("Проверка: Все", null));
        reviewFilter.Items.Add(new ReviewFilterOption("Не проверено", ReviewState.Unreviewed));
        reviewFilter.Items.Add(new ReviewFilterOption("Проверено", ReviewState.Reviewed));
        reviewFilter.Items.Add(new ReviewFilterOption("Требует правки", ReviewState.NeedsFix));
        reviewFilter.Items.Add(new ReviewFilterOption("Пропустить", ReviewState.Skipped));
        reviewFilter.SelectedIndex = 0;
        reviewFilter.SelectionChanged += (_, _) =>
        {
            if (reviewFilter.SelectedItem is ReviewFilterOption option)
            {
                state.ReviewFilter = option.State;
                viewModel.EntriesView.Refresh();
            }
        };

        foreach (var button in FindVisualChildren<Button>(window).Where(x =>
                     ReferenceEquals(x.Command, viewModel.FilterAllCommand) ||
                     ReferenceEquals(x.Command, viewModel.FilterTranslatedCommand) ||
                     ReferenceEquals(x.Command, viewModel.FilterUntranslatedCommand) ||
                     ReferenceEquals(x.Command, viewModel.FilterModifiedCommand) ||
                     ReferenceEquals(x.Command, viewModel.FilterErrorsCommand)))
        {
            button.Click += (_, _) =>
            {
                state.QaFilter = QaFilter.None;
                UpdateQaButtons(state);
                viewModel.EntriesView.Refresh();
            };
        }

        UpdateQaButtons(state);
    }

    private static void ActivateQaFilter(MainViewModel viewModel, State state, QaFilter filter)
    {
        viewModel.StatusFilter = "Все";
        state.QaFilter = state.QaFilter == filter ? QaFilter.None : filter;
        UpdateQaButtons(state);
        viewModel.EntriesView.Refresh();
    }

    private static void UpdateQaButtons(State state)
    {
        foreach (var pair in state.QaButtons)
        {
            pair.Value.FontWeight = pair.Key == state.QaFilter
                ? FontWeights.SemiBold
                : FontWeights.Normal;
            pair.Value.Opacity = pair.Key == state.QaFilter ? 1.0 : 0.78;
        }
    }

    private static bool MatchesQaFilter(object item, QaFilter filter)
    {
        if (filter == QaFilter.None)
            return true;
        if (item is not LocalizationEntry entry)
            return false;

        return filter switch
        {
            QaFilter.Tags => entry.HasTagIssues,
            QaFilter.Placeholders => entry.HasPlaceholderIssues,
            QaFilter.NewLines => entry.HasNewLineIssues,
            _ => true
        };
    }

    private static bool MatchesReviewFilter(MainViewModel viewModel, object item, ReviewState? filter)
    {
        if (filter is null)
            return true;
        if (item is not LocalizationEntry entry || viewModel.ActiveDocument is null)
            return false;

        return ReviewWorkflowService.GetStatus(viewModel.ActiveDocument.FilePath, entry) == filter.Value;
    }

    private static void InstallReviewWorkflow(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox,
        ComboBox reviewStatus,
        State state)
    {
        state.ReviewStatusCombo = reviewStatus;
        reviewStatus.Items.Clear();
        reviewStatus.Items.Add(new ReviewStatusOption("Не проверено", ReviewState.Unreviewed));
        reviewStatus.Items.Add(new ReviewStatusOption("Проверено", ReviewState.Reviewed));
        reviewStatus.Items.Add(new ReviewStatusOption("Требует правки", ReviewState.NeedsFix));
        reviewStatus.Items.Add(new ReviewStatusOption("Пропустить", ReviewState.Skipped));

        reviewStatus.SelectionChanged += (_, _) =>
        {
            if (state.UpdatingReviewStatus ||
                reviewStatus.SelectedItem is not ReviewStatusOption option ||
                viewModel.ActiveDocument is null ||
                viewModel.SelectedEntry is null)
            {
                return;
            }

            CommitTranslation(viewModel, translationBox, state, "Ручная правка");
            ReviewWorkflowService.SetStatus(
                viewModel.ActiveDocument.FilePath,
                viewModel.SelectedEntry,
                option.State);
            viewModel.EntriesView.Refresh();
        };

        var menu = entriesGrid.ContextMenu ?? new ContextMenu();
        entriesGrid.ContextMenu = menu;
        menu.Items.Add(new Separator());

        var statusMenu = new MenuItem { Header = "Статус проверки" };
        foreach (var option in new[]
                 {
                     new ReviewStatusOption("Не проверено", ReviewState.Unreviewed),
                     new ReviewStatusOption("Проверено", ReviewState.Reviewed),
                     new ReviewStatusOption("Требует правки", ReviewState.NeedsFix),
                     new ReviewStatusOption("Пропустить", ReviewState.Skipped)
                 })
        {
            var item = new MenuItem { Header = option.Text };
            item.Click += (_, _) =>
            {
                if (viewModel.ActiveDocument is null)
                    return;

                var selected = entriesGrid.SelectedItems.OfType<LocalizationEntry>().ToList();
                if (selected.Count == 0 && viewModel.SelectedEntry is not null)
                    selected.Add(viewModel.SelectedEntry);
                if (selected.Count == 0)
                    return;

                ReviewWorkflowService.SetStatus(
                    viewModel.ActiveDocument.FilePath,
                    selected,
                    option.State);
                RefreshReviewStatus(viewModel, reviewStatus, state);
                viewModel.EntriesView.Refresh();
            };
            statusMenu.Items.Add(item);
        }
        menu.Items.Add(statusMenu);

        menu.Items.Add(CreateMenuItem("✓ Проверено и дальше", () =>
        {
            CommitTranslation(viewModel, translationBox, state, "Ручная правка");
            SetCurrentReviewStatus(viewModel, ReviewState.Reviewed);
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        }));
        menu.Items.Add(CreateMenuItem("⚠ Требует правки и дальше", () =>
        {
            CommitTranslation(viewModel, translationBox, state, "Ручная правка");
            SetCurrentReviewStatus(viewModel, ReviewState.NeedsFix);
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        }));
        menu.Items.Add(CreateMenuItem("→ Следующая непроверенная", () =>
        {
            CommitTranslation(viewModel, translationBox, state, "Ручная правка");
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        }));
    }

    private static void RefreshReviewStatus(MainViewModel viewModel, ComboBox combo, State state)
    {
        state.UpdatingReviewStatus = true;
        try
        {
            var status = viewModel.ActiveDocument is null || viewModel.SelectedEntry is null
                ? ReviewState.Unreviewed
                : ReviewWorkflowService.GetStatus(viewModel.ActiveDocument.FilePath, viewModel.SelectedEntry);

            combo.SelectedItem = combo.Items
                .OfType<ReviewStatusOption>()
                .FirstOrDefault(x => x.State == status);
            combo.IsEnabled = viewModel.SelectedEntry is not null;
        }
        finally
        {
            state.UpdatingReviewStatus = false;
        }
    }

    private static void SetCurrentReviewStatus(MainViewModel viewModel, ReviewState status)
    {
        if (viewModel.ActiveDocument is null || viewModel.SelectedEntry is null)
            return;
        ReviewWorkflowService.SetStatus(viewModel.ActiveDocument.FilePath, viewModel.SelectedEntry, status);
    }

    private static void NavigateNextUnreviewed(MainViewModel viewModel, DataGrid grid, TextBox translationBox)
    {
        var document = viewModel.ActiveDocument;
        if (document is null || document.Entries.Count == 0)
            return;

        var start = viewModel.SelectedEntry is null ? -1 : document.Entries.IndexOf(viewModel.SelectedEntry);
        LocalizationEntry? target = null;

        for (var offset = 1; offset <= document.Entries.Count; offset++)
        {
            var candidate = document.Entries[(start + offset) % document.Entries.Count];
            if (ReviewWorkflowService.GetStatus(document.FilePath, candidate) == ReviewState.Unreviewed)
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
            return;

        viewModel.SelectedEntry = target;
        viewModel.EntriesView.MoveCurrentTo(target);
        grid.SelectedItem = target;
        grid.UpdateLayout();
        grid.ScrollIntoView(target);
        translationBox.Focus();
    }

    private static void InstallStructureQa(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox,
        Border qaBorder,
        TextBlock qaText,
        Button previous,
        Button next,
        Button restore,
        State state)
    {
        translationBox.TextChanged += (_, _) =>
            RefreshStructureQa(viewModel, translationBox, qaBorder, qaText);

        previous.Click += (_, _) => NavigateStructureError(viewModel, entriesGrid, translationBox, -1, state);
        next.Click += (_, _) => NavigateStructureError(viewModel, entriesGrid, translationBox, 1, state);

        restore.Click += (_, _) =>
        {
            var entry = viewModel.SelectedEntry;
            if (entry is null || string.IsNullOrWhiteSpace(entry.Original))
                return;

            var result = StructureProtectionService.RestoreStructure(entry.Original, translationBox.Text);
            if (!result.Changed)
            {
                AppDialog.Show(result.Message, "Восстановление структуры", MessageBoxButton.OK,
                    result.CanRestore ? MessageBoxImage.Information : MessageBoxImage.Warning, window);
                return;
            }

            translationBox.Text = result.Text;
            CommitTranslation(viewModel, translationBox, state, "Восстановление структуры");
            RefreshStructureQa(viewModel, translationBox, qaBorder, qaText);
        };
    }

    private static void RefreshStructureQa(
        MainViewModel viewModel,
        TextBox translationBox,
        Border border,
        TextBlock text)
    {
        var entry = viewModel.SelectedEntry;
        if (entry is null)
        {
            text.Text = "Структура: выберите строку для проверки.";
            border.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(216, 226, 239));
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            text.Text = "— Исходный текст отсутствует — структура не проверяется.";
            border.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            return;
        }

        var status = StructureProtectionService.Analyze(entry.Original, translationBox.Text);
        text.Text = status.CompactSummary;

        if (status.HasTagIssues || status.HasPlaceholderIssues || status.HasNewLineIssues)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(255, 241, 242));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            text.ToolTip = status.StructuralResult.Summary;
        }
        else
        {
            border.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            text.ToolTip = "Структура совпадает с оригиналом.";
        }
    }

    private static void NavigateStructureError(
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox,
        int direction,
        State state)
    {
        CommitTranslation(viewModel, translationBox, state, "Ручная правка");
        var document = viewModel.ActiveDocument;
        if (document is null || document.Entries.Count == 0)
            return;

        var start = viewModel.SelectedEntry is null ? -1 : document.Entries.IndexOf(viewModel.SelectedEntry);
        LocalizationEntry? target = null;

        for (var offset = 1; offset <= document.Entries.Count; offset++)
        {
            var index = direction > 0
                ? (start + offset + document.Entries.Count) % document.Entries.Count
                : (start - offset + document.Entries.Count * 2) % document.Entries.Count;
            var candidate = document.Entries[index];
            if (candidate.HasStructuralValidationIssues)
            {
                target = candidate;
                break;
            }
        }

        if (target is null)
            return;

        viewModel.StatusFilter = "Все";
        viewModel.SearchText = string.Empty;
        viewModel.SelectedEntry = target;
        viewModel.EntriesView.MoveCurrentTo(target);
        entriesGrid.SelectedItem = target;
        entriesGrid.UpdateLayout();
        entriesGrid.ScrollIntoView(target);
        translationBox.Focus();
    }

    private static void InstallProtection(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        CheckBox protection,
        State state)
    {
        translationBox.PreviewTextInput += (_, args) =>
        {
            if (protection.IsChecked != true || viewModel.SelectedEntry is not LocalizationEntry entry ||
                string.IsNullOrWhiteSpace(entry.Original))
                return;

            var proposed = ReplaceSelection(translationBox, args.Text);
            if (StructureProtectionService.WouldWorsenStructure(entry.Original, translationBox.Text, proposed))
                args.Handled = true;
        };

        translationBox.PreviewKeyDown += (_, args) =>
        {
            if (protection.IsChecked != true || viewModel.SelectedEntry is not LocalizationEntry entry ||
                string.IsNullOrWhiteSpace(entry.Original))
                return;

            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                args.Handled = true;
                AutoCorrectCurrent(window, viewModel, translationBox, state);
                return;
            }

            if (args.Key is not (Key.Back or Key.Delete) &&
                !(args.Key == Key.X && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
                return;

            var proposed = args.Key switch
            {
                Key.Back => DeleteBackward(translationBox, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)),
                Key.Delete => DeleteForward(translationBox, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)),
                _ => ReplaceSelection(translationBox, string.Empty)
            };

            if (StructureProtectionService.WouldWorsenStructure(entry.Original, translationBox.Text, proposed))
                args.Handled = true;
        };

        DataObject.AddPastingHandler(translationBox, (_, args) =>
        {
            if (protection.IsChecked != true || viewModel.SelectedEntry is not LocalizationEntry entry ||
                string.IsNullOrWhiteSpace(entry.Original))
                return;

            var pasted = args.DataObject.GetData(DataFormats.UnicodeText) as string
                         ?? args.DataObject.GetData(DataFormats.Text) as string
                         ?? string.Empty;
            var proposed = ReplaceSelection(translationBox, pasted);

            if (!StructureProtectionService.WouldWorsenStructure(entry.Original, translationBox.Text, proposed))
                return;

            var answer = AppDialog.Show(
                "После вставки структура тегов/плейсхолдеров станет хуже. Всё равно вставить текст?",
                "Защита структуры",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                window);
            if (answer != MessageBoxResult.Yes)
                args.CancelCommand();
        });
    }

    private static void InstallAutoCorrection(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Button autoButton,
        Button bulkButton,
        State state)
    {
        autoButton.ToolTip = "Безопасное исправление текущей строки. Ctrl+Enter.";
        bulkButton.ToolTip = "Анализ всего текущего файла с прогрессом, отменой и предпросмотром.";

        autoButton.Click += (_, _) => AutoCorrectCurrent(window, viewModel, translationBox, state);
        bulkButton.Click += async (_, _) => await AutoCorrectBulkAsync(window, viewModel, translationBox, state);
    }

    private static void AutoCorrectCurrent(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        CommitTranslation(viewModel, translationBox, state, "Ручная правка");
        var entry = viewModel.SelectedEntry;
        if (entry is null)
            return;

        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
        if (!result.HasChanges)
        {
            AppDialog.Show(
                result.RequiresReview ? result.ReviewReason : "Безопасных автоисправлений не найдено.",
                "Автоисправление",
                MessageBoxButton.OK,
                result.RequiresReview ? MessageBoxImage.Warning : MessageBoxImage.Information,
                window);
            return;
        }

        var before = entry.Translation;
        EntryHistoryService.Record(entry, before, result.CorrectedText, "Автоисправление");
        entry.Translation = result.CorrectedText;
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        state.TranslationBaseline = entry.Translation;
    }

    private static async Task AutoCorrectBulkAsync(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        CommitTranslation(viewModel, translationBox, state, "Ручная правка");
        var document = viewModel.ActiveDocument;
        if (document is null)
            return;

        var progressWindow = new OperationProgressWindow(
            "Анализ файла",
            $"Подготовка анализа {document.Entries.Count:N0} строк…")
        {
            Owner = window
        };
        progressWindow.Show();

        TranslationCorrectionPlan plan;
        try
        {
            var progress = new Progress<TranslationCorrectionProgress>(p =>
                progressWindow.Report(
                    $"Анализ: {p.Processed:N0} / {p.Total:N0} · изменений {p.Changes:N0} · на проверку {p.ReviewItems:N0}",
                    p.Processed,
                    p.Total));

            plan = await TranslationAutoCorrectionProgressService.BuildPlanAsync(
                document.Entries,
                progress,
                cancellationToken: progressWindow.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            progressWindow.Complete();
            return;
        }
        catch (Exception ex)
        {
            progressWindow.Complete();
            AppDialog.Show(ex.Message, "Ошибка автоисправления", MessageBoxButton.OK, MessageBoxImage.Error, window);
            return;
        }

        progressWindow.Complete();

        if (plan.Changes.Count == 0)
        {
            AppDialog.Show(
                $"Проверено строк: {document.Entries.Count:N0}. Безопасных изменений нет. Неоднозначных случаев: {plan.ReviewItems.Count:N0}.",
                "Автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        var preview = new TranslationCorrectionPreviewWindow(document.Entries.Count, plan)
        {
            Owner = window
        };
        if (preview.ShowDialog() != true || preview.SelectedChanges.Count == 0)
            return;

        try
        {
            SnapshotService.CreateSnapshot(document, "before-bulk-autocorrect");
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                "Изменения не применены: не удалось создать защитный снимок.\n\n" + ex.Message,
                "Массовое автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                window);
            return;
        }

        var applied = ApplyBulkChanges(viewModel, preview.SelectedChanges, state, "Массовое автоисправление");
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        viewModel.EntriesView.Refresh();
        ProjectHistoryService.Record(document.FilePath, "Массовое автоисправление", applied.Count,
            $"Исправлений: {applied.Sum(x => x.FixCount):N0}; неоднозначных: {plan.ReviewItems.Count:N0}");

        AppDialog.Show(
            $"Исправлено строк: {applied.Count:N0}. Неоднозначных случаев оставлено для QA: {plan.ReviewItems.Count:N0}.\n\nОперацию можно отменить одним Ctrl+Z.",
            "Массовое автоисправление",
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            window);
    }

    private static IReadOnlyList<TranslationCorrectionChange> ApplyBulkChanges(
        MainViewModel viewModel,
        IReadOnlyList<TranslationCorrectionChange> requested,
        State state,
        string reason)
    {
        var session = GetActiveSession(viewModel);
        if (session is null)
            return [];

        var applied = new List<TranslationCorrectionChange>();
        var baseline = session.UndoStack.Count;
        session.HistoryChangeInProgress = true;

        try
        {
            using (EntryHistoryService.BeginOperation(reason))
            {
                foreach (var change in requested)
                {
                    if (!string.Equals(change.Entry.Translation, change.Before, StringComparison.Ordinal))
                        continue;

                    EntryHistoryService.Record(change.Entry, change.Before, change.After, reason);
                    change.Entry.Translation = change.After;
                    applied.Add(change);
                }
            }
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }

        if (applied.Count > 0)
        {
            state.BulkUndo.Push(new BulkHistoryOperation(applied, baseline));
            state.BulkRedo.Clear();
        }

        return applied;
    }

    private static void InstallAi(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox,
        Button aiButton,
        State state)
    {
        aiButton.ToolTip = "Левый клик — текущая строка. Правый клик — пакетные режимы и настройки.";
        aiButton.Click += async (_, _) => await ReviewCurrentAiAsync(window, viewModel, translationBox, aiButton, state);

        var menu = new ContextMenu();
        menu.Items.Add(CreateAsyncMenuItem("Текущая строка", () => ReviewCurrentAiAsync(window, viewModel, translationBox, aiButton, state)));
        menu.Items.Add(CreateAsyncMenuItem("Выделенные строки", () => ReviewAiBatchAsync(
            window, viewModel, translationBox, aiButton, state,
            entriesGrid.SelectedItems.OfType<LocalizationEntry>().ToList(), "выделенные строки")));
        menu.Items.Add(CreateAsyncMenuItem("QA-ошибки", () => ReviewAiBatchAsync(
            window, viewModel, translationBox, aiButton, state,
            viewModel.ActiveDocument?.Entries.Where(x => x.HasValidationIssues && !x.HasSourceMissingIssue).ToList() ?? [],
            "QA-ошибки")));
        menu.Items.Add(CreateAsyncMenuItem("Требует правки", () =>
        {
            var document = viewModel.ActiveDocument;
            var candidates = document is null
                ? []
                : document.Entries.Where(x => ReviewWorkflowService.GetStatus(document.FilePath, x) == ReviewState.NeedsFix).ToList();
            return ReviewAiBatchAsync(window, viewModel, translationBox, aiButton, state, candidates, "Требует правки");
        }));
        menu.Items.Add(CreateAsyncMenuItem("Подозрительные строки", () => ReviewAiBatchAsync(
            window, viewModel, translationBox, aiButton, state,
            viewModel.ActiveDocument?.Entries.Where(x => x.HasSuspiciousLengthIssue || x.HasSameAsSourceIssue || x.HasProfileRuleIssue || x.HasGlossaryIssue).ToList() ?? [],
            "подозрительные строки")));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Настроить ИИ…", () => ConfigureAi(window)));
        aiButton.ContextMenu = menu;
    }

    private static async Task ReviewCurrentAiAsync(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Button aiButton,
        State state)
    {
        CommitTranslation(viewModel, translationBox, state, "Ручная правка");
        var entry = viewModel.SelectedEntry;
        if (entry is null || !EnsureAiConfigured(window))
            return;

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            AppDialog.Show("У строки нет Original — ИИ-проверка отключена.", "ИИ-проверка",
                MessageBoxButton.OK, MessageBoxImage.Warning, window);
            return;
        }

        aiButton.IsEnabled = false;
        var old = aiButton.Content;
        aiButton.Content = "ИИ…";
        try
        {
            var result = await ReviewAiWithRetryAsync(entry, CancellationToken.None);
            var dialog = new AiReviewPreviewWindow(entry, result) { Owner = window };
            dialog.ShowDialog();
            if (!dialog.ApplySuggestion || !result.Changed)
                return;

            var before = entry.Translation;
            EntryHistoryService.Record(entry, before, result.Translation, "ИИ-исправление");
            entry.Translation = result.Translation;
            translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            state.TranslationBaseline = entry.Translation;
        }
        catch (Exception ex)
        {
            AppDialog.Show(ex.Message, "Ошибка ИИ-проверки", MessageBoxButton.OK, MessageBoxImage.Error, window);
        }
        finally
        {
            aiButton.Content = old;
            aiButton.IsEnabled = true;
        }
    }

    private static async Task ReviewAiBatchAsync(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Button aiButton,
        State state,
        IReadOnlyList<LocalizationEntry> requested,
        string label)
    {
        CommitTranslation(viewModel, translationBox, state, "Ручная правка");
        var document = viewModel.ActiveDocument;
        if (document is null || !EnsureAiConfigured(window))
            return;

        var settings = EditorSettingsService.Current;
        var candidates = requested
            .Distinct()
            .Where(x => !string.IsNullOrWhiteSpace(x.Original))
            .Take(settings.AiBatchLimit)
            .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show("Подходящих строк для ИИ-проверки нет.", "ИИ-проверка",
                MessageBoxButton.OK, MessageBoxImage.Information, window);
            return;
        }

        var approximateTokens = candidates.Sum(EstimateAiTokens);
        var answer = AppDialog.Show(
            $"Режим: {label}.\nСтрок будет отправлено: {candidates.Count:N0}.\nОценка объёма: ~{approximateTokens:N0} токенов.\nПараллельность: {settings.AiConcurrency}.\n\nДенежная стоимость зависит от текущего тарифа выбранной модели. Начать?",
            "Пакетная ИИ-проверка",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            window);
        if (answer != MessageBoxResult.Yes)
            return;

        aiButton.IsEnabled = false;
        var progressWindow = new OperationProgressWindow("ИИ-проверка", $"0 / {candidates.Count:N0}") { Owner = window };
        progressWindow.Show();

        var suggestions = new List<AiBatchSuggestion>();
        var failures = 0;
        var processed = 0;
        var sync = new object();
        using var semaphore = new SemaphoreSlim(settings.AiConcurrency);

        try
        {
            var tasks = candidates.Select(async entry =>
            {
                await semaphore.WaitAsync(progressWindow.CancellationToken);
                try
                {
                    var result = await ReviewAiWithRetryAsync(entry, progressWindow.CancellationToken);
                    if (result.Changed)
                    {
                        lock (sync)
                        {
                            suggestions.Add(new AiBatchSuggestion
                            {
                                Entry = entry,
                                Before = entry.Translation,
                                After = result.Translation,
                                Reason = result.Reason
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref failures);
                }
                finally
                {
                    semaphore.Release();
                    var done = Interlocked.Increment(ref processed);
                    progressWindow.Report(
                        $"ИИ-проверка: {done:N0} / {candidates.Count:N0} · предложений {suggestions.Count:N0} · ошибок {failures:N0}",
                        done,
                        candidates.Count);
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            progressWindow.Complete();
            aiButton.IsEnabled = true;
            return;
        }
        finally
        {
            progressWindow.Complete();
            aiButton.IsEnabled = true;
        }

        if (suggestions.Count == 0)
        {
            AppDialog.Show($"ИИ не предложил изменений. Ошибок: {failures:N0}.", "ИИ-проверка",
                MessageBoxButton.OK, failures == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning, window);
            return;
        }

        var preview = new AiBatchPreviewWindow(suggestions.OrderBy(x => x.Entry.Index).ToList()) { Owner = window };
        if (preview.ShowDialog() != true || preview.SelectedSuggestions.Count == 0)
            return;

        try
        {
            SnapshotService.CreateSnapshot(document, "before-ai-review");
        }
        catch (Exception ex)
        {
            AppDialog.Show("ИИ-изменения не применены: снимок создать не удалось.\n\n" + ex.Message,
                "ИИ-проверка", MessageBoxButton.OK, MessageBoxImage.Error, window);
            return;
        }

        var correctionChanges = preview.SelectedSuggestions
            .Select(x => new TranslationCorrectionChange(x.Entry, x.Before, x.After, 1, ["ИИ-проверка"]))
            .ToList();
        var applied = ApplyBulkChanges(viewModel, correctionChanges, state, "ИИ-исправление");
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        viewModel.EntriesView.Refresh();
        ProjectHistoryService.Record(document.FilePath, "Пакетная ИИ-проверка", applied.Count,
            $"Проанализировано: {candidates.Count:N0}; ошибок API: {failures:N0}");
    }

    private static async Task<AiReviewResult> ReviewAiWithRetryAsync(
        LocalizationEntry entry,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await AiCorrectionService.ReviewAsync(entry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
        throw last ?? new InvalidOperationException("ИИ-проверка не выполнена.");
    }

    private static int EstimateAiTokens(LocalizationEntry entry)
        => Math.Max(80, (entry.Original.Length + entry.Translation.Length) / 3 + 180);

    private static bool EnsureAiConfigured(MainWindow window)
        => AiCorrectionService.IsConfigured || ConfigureAi(window);

    private static bool ConfigureAi(MainWindow window)
    {
        var dialog = new AiSettingsWindow { Owner = window };
        if (dialog.ShowDialog() != true)
            return false;
        AiCorrectionService.ConfigureSession(dialog.ApiKey, dialog.Model);
        return AiCorrectionService.IsConfigured;
    }

    private static void InstallBulkUndo(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        State state)
    {
        window.PreviewKeyDown += (_, args) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (args.Key == Key.Z)
            {
                CommitTranslation(viewModel, translationBox, state, "Ручная правка");
                if (TryUndoBulk(viewModel, translationBox, state))
                    args.Handled = true;
            }
            else if (args.Key == Key.Y)
            {
                CommitTranslation(viewModel, translationBox, state, "Ручная правка");
                if (TryRedoBulk(viewModel, translationBox, state))
                    args.Handled = true;
            }
        };
    }

    private static bool TryUndoBulk(MainViewModel viewModel, TextBox translationBox, State state)
    {
        if (state.BulkUndo.Count == 0)
            return false;

        var session = GetActiveSession(viewModel);
        if (session is null)
            return false;

        var operation = state.BulkUndo.Peek();
        if (session.UndoStack.Count > operation.NormalUndoBaseline)
            return false;

        state.BulkUndo.Pop();
        session.HistoryChangeInProgress = true;
        try
        {
            foreach (var change in operation.Changes.Reverse())
            {
                if (string.Equals(change.Entry.Translation, change.After, StringComparison.Ordinal))
                    change.Entry.Translation = change.Before;
            }
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }
        state.BulkRedo.Push(operation);
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        viewModel.EntriesView.Refresh();
        return true;
    }

    private static bool TryRedoBulk(MainViewModel viewModel, TextBox translationBox, State state)
    {
        if (state.BulkRedo.Count == 0)
            return false;

        var session = GetActiveSession(viewModel);
        if (session is null)
            return false;

        var operation = state.BulkRedo.Pop();
        session.HistoryChangeInProgress = true;
        try
        {
            foreach (var change in operation.Changes)
            {
                if (string.Equals(change.Entry.Translation, change.Before, StringComparison.Ordinal))
                    change.Entry.Translation = change.After;
            }
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }
        state.BulkUndo.Push(operation);
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        viewModel.EntriesView.Refresh();
        return true;
    }

    private static void CommitTranslation(
        MainViewModel viewModel,
        TextBox translationBox,
        State state,
        string reason)
    {
        var entry = viewModel.SelectedEntry;
        if (entry is null)
            return;

        var before = entry.Translation;
        translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var after = entry.Translation;

        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            EntryHistoryService.Record(entry, before, after, reason);
            if (viewModel.ActiveDocument is not null)
                ReviewWorkflowService.OnTranslationChanged(viewModel.ActiveDocument.FilePath, entry);
            state.BulkRedo.Clear();
        }

        state.TranslationBaseline = after;
    }

    private static string ReplaceSelection(TextBox box, string replacement)
        => box.Text[..box.SelectionStart] + replacement + box.Text[(box.SelectionStart + box.SelectionLength)..];

    private static string DeleteBackward(TextBox box, bool word)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);
        if (box.CaretIndex <= 0)
            return box.Text;

        var start = box.CaretIndex - 1;
        if (word)
        {
            while (start > 0 && char.IsWhiteSpace(box.Text[start]))
                start--;
            while (start > 0 && !char.IsWhiteSpace(box.Text[start - 1]))
                start--;
        }
        return box.Text.Remove(start, box.CaretIndex - start);
    }

    private static string DeleteForward(TextBox box, bool word)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);
        if (box.CaretIndex >= box.Text.Length)
            return box.Text;

        var end = box.CaretIndex + 1;
        if (word)
        {
            while (end < box.Text.Length && char.IsWhiteSpace(box.Text[end]))
                end++;
            while (end < box.Text.Length && !char.IsWhiteSpace(box.Text[end]))
                end++;
        }
        return box.Text.Remove(box.CaretIndex, end - box.CaretIndex);
    }

    private static bool IsInside(DependencyObject source, DependencyObject ancestor)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            try
            {
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            catch
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return false;
    }

    private static DocumentSession? GetActiveSession(MainViewModel viewModel)
        => typeof(MainViewModel)
            .GetField("_activeSession", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(viewModel) as DocumentSession;

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem CreateAsyncMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private enum QaFilter
    {
        None,
        Tags,
        Placeholders,
        NewLines
    }

    private sealed class State
    {
        public MainViewModel? ViewModel { get; set; }
        public TextBox? TranslationBox { get; set; }
        public DataGrid? EntriesGrid { get; set; }
        public Border? QaBorder { get; set; }
        public TextBlock? QaText { get; set; }
        public CheckBox? ProtectionToggle { get; set; }
        public ComboBox? ReviewStatusCombo { get; set; }
        public Predicate<object>? BaseFilter { get; set; }
        public QaFilter QaFilter { get; set; }
        public ReviewState? ReviewFilter { get; set; }
        public bool UpdatingReviewStatus { get; set; }
        public string TranslationBaseline { get; set; } = string.Empty;
        public Dictionary<QaFilter, Button> QaButtons { get; } = new();
        public Stack<BulkHistoryOperation> BulkUndo { get; } = new();
        public Stack<BulkHistoryOperation> BulkRedo { get; } = new();
    }

    private sealed record ReviewFilterOption(string Text, ReviewState? State)
    {
        public override string ToString() => Text;
    }

    private sealed record ReviewStatusOption(string Text, ReviewState State)
    {
        public override string ToString() => Text;
    }

    private sealed record BulkHistoryOperation(
        IReadOnlyList<TranslationCorrectionChange> Changes,
        int NormalUndoBaseline);
}
