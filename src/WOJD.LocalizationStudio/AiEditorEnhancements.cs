using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class AiEditorEnhancements
{
    private const int MaxBatchSize = 100;

    private static readonly ConditionalWeakTable<MainWindow, object>
        InstalledWindows = new();

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
        if (sender is not MainWindow window ||
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        InstalledWindows.Add(window, new object());
        window.Dispatcher.BeginInvoke(
            () => Install(window),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(MainWindow window)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox ||
            window.FindName("EntriesGrid") is not DataGrid entriesGrid)
        {
            return;
        }

        var aiButton = FindVisualChildren<Button>(window)
            .FirstOrDefault(x =>
                string.Equals(
                    x.Content?.ToString(),
                    "ИИ-исправление",
                    StringComparison.Ordinal));

        if (aiButton is null)
            return;

        aiButton.IsEnabled = true;
        aiButton.Content = "ИИ-проверка";
        aiButton.ToolTip =
            "Левый клик — проверить текущую строку. Правый клик — пакетные режимы и настройка.";
        aiButton.Click += async (_, _) =>
            await ReviewCurrentAsync(
                window,
                viewModel,
                translationBox,
                aiButton);

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem(
            "Текущая строка",
            async () => await ReviewCurrentAsync(
                window,
                viewModel,
                translationBox,
                aiButton)));
        menu.Items.Add(CreateMenuItem(
            "Выделенные строки",
            async () => await ReviewBatchAsync(
                window,
                viewModel,
                translationBox,
                aiButton,
                entriesGrid.SelectedItems.OfType<LocalizationEntry>().ToList(),
                "выделенные строки")));
        menu.Items.Add(CreateMenuItem(
            "QA-ошибки",
            async () => await ReviewBatchAsync(
                window,
                viewModel,
                translationBox,
                aiButton,
                viewModel.ActiveDocument?.Entries
                    .Where(x => x.HasValidationIssues && !x.HasSourceMissingIssue)
                    .ToList()
                ?? [],
                "QA-ошибки")));
        menu.Items.Add(CreateMenuItem(
            "Требует правки",
            async () =>
            {
                var document = viewModel.ActiveDocument;
                var candidates = document is null
                    ? []
                    : document.Entries
                        .Where(x =>
                            ReviewWorkflowService.GetStatus(
                                document.FilePath,
                                x) == ReviewState.NeedsFix)
                        .ToList();

                await ReviewBatchAsync(
                    window,
                    viewModel,
                    translationBox,
                    aiButton,
                    candidates,
                    "строки «Требует правки»");
            }));
        menu.Items.Add(CreateMenuItem(
            "Подозрительные строки",
            async () => await ReviewBatchAsync(
                window,
                viewModel,
                translationBox,
                aiButton,
                viewModel.ActiveDocument?.Entries
                    .Where(x =>
                        x.HasSuspiciousLengthIssue ||
                        x.HasSameAsSourceIssue ||
                        x.HasProfileRuleIssue)
                    .ToList()
                ?? [],
                "подозрительные строки")));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(
            "Настроить ИИ…",
            () =>
            {
                Configure(window);
                return Task.CompletedTask;
            }));

        aiButton.ContextMenu = menu;
    }

    private static async Task ReviewCurrentAsync(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Button aiButton)
    {
        CommitTranslation(translationBox);

        var entry = viewModel.SelectedEntry;
        if (entry is null)
            return;

        if (!EnsureConfigured(window))
            return;

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            AppDialog.Show(
                "У строки нет исходного текста. ИИ-проверка отключена, потому что без Original нельзя надёжно оценить перевод.",
                "ИИ-проверка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                window);
            return;
        }

        var oldContent = aiButton.Content;
        aiButton.IsEnabled = false;
        aiButton.Content = "ИИ…";

        try
        {
            var result = await AiCorrectionService.ReviewAsync(entry);
            var dialog = new AiReviewPreviewWindow(entry, result)
            {
                Owner = window
            };

            dialog.ShowDialog();

            if (!dialog.ApplySuggestion || !result.Changed)
                return;

            var before = entry.Translation;

            using (EntryHistoryService.BeginOperation("ИИ-исправление"))
            {
                EntryHistoryService.Record(
                    entry,
                    before,
                    result.Translation,
                    "ИИ-исправление");
                entry.Translation = result.Translation;
            }

            translationBox
                .GetBindingExpression(TextBox.TextProperty)?
                .UpdateTarget();
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                ex.Message,
                "Ошибка ИИ-проверки",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                window);
        }
        finally
        {
            aiButton.Content = oldContent;
            aiButton.IsEnabled = true;
        }
    }

    private static async Task ReviewBatchAsync(
        MainWindow window,
        MainViewModel viewModel,
        TextBox translationBox,
        Button aiButton,
        IReadOnlyList<LocalizationEntry> requested,
        string label)
    {
        CommitTranslation(translationBox);

        var document = viewModel.ActiveDocument;
        if (document is null)
            return;

        var candidates = requested
            .Distinct()
            .Where(x => !string.IsNullOrWhiteSpace(x.Original))
            .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(
                "Подходящих строк для ИИ-проверки нет.",
                "ИИ-проверка",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        if (!EnsureConfigured(window))
            return;

        if (candidates.Count > MaxBatchSize)
        {
            var answer = AppDialog.Show(
                $"Найдено строк: {candidates.Count:N0}. За один запуск редактор отправляет не более {MaxBatchSize} строк, чтобы избежать случайно большого расхода API.\n\nПроверить первые {MaxBatchSize}?",
                "Пакетная ИИ-проверка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                window);

            if (answer != MessageBoxResult.Yes)
                return;

            candidates = candidates.Take(MaxBatchSize).ToList();
        }
        else
        {
            var answer = AppDialog.Show(
                $"Режим: {label}.\nСтрок будет отправлено в API: {candidates.Count:N0}.\n\nНачать проверку?",
                "Пакетная ИИ-проверка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                window);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        var oldContent = aiButton.Content;
        aiButton.IsEnabled = false;
        var suggestions = new List<AiBatchSuggestion>();
        var failures = 0;

        try
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                aiButton.Content = $"ИИ {i + 1}/{candidates.Count}";

                try
                {
                    var entry = candidates[i];
                    var result = await AiCorrectionService.ReviewAsync(entry);

                    if (result.Changed)
                    {
                        suggestions.Add(
                            new AiBatchSuggestion
                            {
                                Entry = entry,
                                Before = entry.Translation,
                                After = result.Translation,
                                Reason = result.Reason
                            });
                    }
                }
                catch
                {
                    failures++;
                }
            }
        }
        finally
        {
            aiButton.Content = oldContent;
            aiButton.IsEnabled = true;
        }

        if (suggestions.Count == 0)
        {
            AppDialog.Show(
                failures == 0
                    ? "ИИ не предложил изменений."
                    : $"Изменений не предложено. Ошибок API/ответа: {failures:N0}.",
                "Пакетная ИИ-проверка",
                MessageBoxButton.OK,
                failures == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning,
                window);
            return;
        }

        var preview = new AiBatchPreviewWindow(suggestions)
        {
            Owner = window
        };

        if (preview.ShowDialog() != true)
            return;

        var selected = preview.SelectedSuggestions;
        if (selected.Count == 0)
            return;

        try
        {
            SnapshotService.CreateSnapshot(document, "before-ai-review");
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                "ИИ-изменения не применены: не удалось создать защитный снимок.\n\n" + ex.Message,
                "Снимок перед ИИ",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                window);
            return;
        }

        var applied = 0;

        using (EntryHistoryService.BeginOperation("ИИ-исправление"))
        {
            foreach (var suggestion in selected)
            {
                if (!string.Equals(
                        suggestion.Entry.Translation,
                        suggestion.Before,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                EntryHistoryService.Record(
                    suggestion.Entry,
                    suggestion.Before,
                    suggestion.After,
                    "ИИ-исправление");
                suggestion.Entry.Translation = suggestion.After;
                applied++;
            }
        }

        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateTarget();
        viewModel.EntriesView.Refresh();

        AppDialog.Show(
            $"Применено ИИ-предложений: {applied:N0}.\nОшибок при анализе: {failures:N0}.\nПеред применением создан снимок файла.",
            "ИИ-проверка завершена",
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            window);
    }

    private static bool EnsureConfigured(MainWindow window)
    {
        if (AiCorrectionService.IsConfigured)
            return true;

        return Configure(window);
    }

    private static bool Configure(MainWindow window)
    {
        var dialog = new AiSettingsWindow
        {
            Owner = window
        };

        if (dialog.ShowDialog() != true)
            return false;

        AiCorrectionService.ConfigureSession(
            dialog.ApiKey,
            dialog.Model);
        return AiCorrectionService.IsConfigured;
    }

    private static MenuItem CreateMenuItem(
        string header,
        Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static void CommitTranslation(TextBox translationBox)
        => translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateSource();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
