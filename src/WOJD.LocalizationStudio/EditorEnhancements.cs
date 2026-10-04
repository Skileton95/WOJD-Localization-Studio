using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class EditorEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, EditorState>
        InstalledWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        var state = new EditorState();
        InstalledWindows.Add(window, state);

        if (window.FindName("TranslationBox") is not TextBox translationBox)
            return;

        InstallSafeTranslationBinding(window, translationBox);
        InstallAutoCorrectionControls(window, translationBox, state);
        InstallHistoryCommands(window, translationBox, state);
    }

    private static void InstallSafeTranslationBinding(
        MainWindow window,
        TextBox translationBox)
    {
        BindingOperations.SetBinding(
            translationBox,
            TextBox.TextProperty,
            new Binding("SelectedEntry.Translation")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
                TargetNullValue = string.Empty,
                FallbackValue = string.Empty
            });

        translationBox.LostKeyboardFocus += (_, _) =>
            CommitTranslation(translationBox);

        window.Deactivated += (_, _) =>
            CommitTranslation(translationBox);

        window.Closing += (_, _) =>
            CommitTranslation(translationBox);

        window.PreviewMouseDown += (_, args) =>
        {
            if (!translationBox.IsKeyboardFocusWithin)
                return;

            if (args.OriginalSource is not DependencyObject source ||
                IsInside(source, translationBox))
            {
                return;
            }

            CommitTranslation(translationBox);
        };

        window.PreviewKeyDown += (_, args) =>
        {
            if (!translationBox.IsKeyboardFocusWithin)
                return;

            var modifiers = Keyboard.Modifiers;
            var ctrl = (modifiers & ModifierKeys.Control) != 0;

            if (ctrl && args.Key == Key.Enter)
            {
                AutoCorrectCurrent(window, translationBox);
                args.Handled = true;
                return;
            }

            var commandMayChangeContext =
                args.Key == Key.F6 ||
                (ctrl &&
                 (args.Key == Key.S ||
                  args.Key == Key.G ||
                  args.Key == Key.O ||
                  args.Key == Key.K));

            if (commandMayChangeContext)
                CommitTranslation(translationBox);
        };
    }

    private static void InstallAutoCorrectionControls(
        MainWindow window,
        TextBox translationBox,
        EditorState state)
    {
        if (translationBox.Parent is not Border editorBorder ||
            editorBorder.Parent is not Grid editorGrid)
        {
            return;
        }

        var footer =
            editorGrid.Children
                .OfType<Grid>()
                .FirstOrDefault(x =>
                    Grid.GetColumn(x) == 2 &&
                    Grid.GetRow(x) == 2);

        if (footer is null)
            return;

        var obsoleteButtons =
            footer.Children
                .OfType<Button>()
                .Where(button =>
                    button.Content is string text &&
                    (text == "Предыдущая" ||
                     text == "Следующая" ||
                     text == "Применить"))
                .ToList();

        foreach (var button in obsoleteButtons)
            footer.Children.Remove(button);

        var autoCorrectButton =
            CreateButton(
                window,
                "Автоисправить",
                "PrimaryButton",
                "Исправляет безопасные ошибки строки, включая однозначно восстанавливаемые теги и плейсхолдеры из оригинала.");

        autoCorrectButton.Click += (_, _) =>
            AutoCorrectCurrent(window, translationBox);

        Grid.SetColumn(autoCorrectButton, 1);
        footer.Children.Add(autoCorrectButton);

        var bulkButton =
            CreateButton(
                window,
                "Массово…",
                "SecondaryButton",
                "Анализирует весь открытый файл и показывает предпросмотр перед применением.");

        bulkButton.Margin = new Thickness(8, 0, 8, 0);
        bulkButton.Click += (_, _) =>
            AutoCorrectBulk(window, translationBox, state);

        Grid.SetColumn(bulkButton, 2);
        footer.Children.Add(bulkButton);

        var aiButton =
            CreateButton(
                window,
                "ИИ-исправление",
                "SecondaryButton",
                "ИИ-провайдер будет подключён к тому же механизму предпросмотра и пакетного применения.");

        aiButton.IsEnabled = false;
        Grid.SetColumn(aiButton, 3);
        footer.Children.Add(aiButton);
    }

    private static void InstallHistoryCommands(
        MainWindow window,
        TextBox translationBox,
        EditorState state)
    {
        if (window.DataContext is not MainViewModel viewModel)
            return;

        var undoCommand = new DelegateCommand(
            () => Undo(window, translationBox, viewModel, state));
        var redoCommand = new DelegateCommand(
            () => Redo(window, translationBox, viewModel, state));

        foreach (var binding in window.InputBindings.OfType<KeyBinding>())
        {
            if (binding.Key == Key.Z && binding.Modifiers == ModifierKeys.Control)
                binding.Command = undoCommand;

            if (binding.Key == Key.Y && binding.Modifiers == ModifierKeys.Control)
                binding.Command = redoCommand;
        }

        var menu = FindVisualChild<Menu>(window);
        var editMenu = menu?.Items
            .OfType<MenuItem>()
            .FirstOrDefault(x =>
                string.Equals(
                    x.Header?.ToString(),
                    "Правка",
                    StringComparison.Ordinal));

        if (editMenu is null)
            return;

        foreach (var item in editMenu.Items.OfType<MenuItem>())
        {
            var header = item.Header?.ToString();

            if (string.Equals(header, "Отменить", StringComparison.Ordinal))
                item.Command = undoCommand;
            else if (string.Equals(header, "Повторить", StringComparison.Ordinal))
                item.Command = redoCommand;
        }
    }

    private static Button CreateButton(
        MainWindow window,
        string content,
        string styleKey,
        string toolTip)
    {
        var button =
            new Button
            {
                Content = content,
                Padding = new Thickness(14, 8, 14, 8),
                ToolTip = toolTip
            };

        if (window.TryFindResource(styleKey) is Style style)
            button.Style = style;

        return button;
    }

    private static void AutoCorrectCurrent(
        MainWindow window,
        TextBox translationBox)
    {
        CommitTranslation(translationBox);

        if (window.DataContext is not MainViewModel viewModel ||
            viewModel.SelectedEntry is not LocalizationEntry entry)
        {
            return;
        }

        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);

        if (!result.HasChanges)
        {
            AppDialog.Show(
                result.RequiresReview
                    ? "Ошибка структуры найдена, но безопасно исправить её автоматически нельзя.\n\n" +
                      result.ReviewReason
                    : "Безопасных автоисправлений для этой строки не найдено.",
                "Автоисправление",
                MessageBoxButton.OK,
                result.RequiresReview
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information,
                window);
            return;
        }

        entry.Translation = result.CorrectedText;

        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateTarget();

        if (result.RequiresReview)
        {
            AppDialog.Show(
                "Безопасная часть исправлена, но строка всё ещё требует проверки структуры:\n\n" +
                result.ReviewReason,
                "Автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                window);
        }
    }

    private static void AutoCorrectBulk(
        MainWindow window,
        TextBox translationBox,
        EditorState state)
    {
        CommitTranslation(translationBox);

        if (window.DataContext is not MainViewModel viewModel ||
            viewModel.ActiveDocument is not LocalizationDocument document)
        {
            AppDialog.Show(
                "Сначала откройте файл локализации.",
                "Массовое автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        var candidates = document.Entries.Distinct().ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(
                "В текущем файле нет строк для автоисправления.",
                "Массовое автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        TranslationCorrectionPlan plan;

        try
        {
            plan = TranslationAutoCorrectionService.BuildPlan(
                candidates,
                TranslationAutoCorrectionService.RuleBased);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (plan.AffectedEntries == 0)
        {
            AppDialog.Show(
                plan.ReviewItems.Count == 0
                    ? $"Проверен весь файл: {candidates.Count:N0} строк.\nБезопасных автоисправлений не найдено."
                    : $"Проверен весь файл: {candidates.Count:N0} строк.\n" +
                      $"Автоматически исправлять нечего. Требуют ручной проверки: {plan.ReviewItems.Count:N0}.",
                "Массовое автоисправление",
                MessageBoxButton.OK,
                plan.ReviewItems.Count == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning,
                window);
            return;
        }

        var preview =
            new TranslationCorrectionPreviewWindow(
                candidates.Count,
                plan)
            {
                Owner = window
            };

        if (preview.ShowDialog() != true ||
            preview.SelectedChanges.Count == 0)
        {
            return;
        }

        var previousStatusFilter = viewModel.StatusFilter;
        var previousSearchText = viewModel.SearchText;
        var session = GetActiveSession(viewModel);
        var normalUndoBaseline = session?.UndoStack.Count ?? 0;

        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            viewModel.StatusFilter = "Все";
            viewModel.SearchText = string.Empty;

            if (session is not null)
                session.HistoryChangeInProgress = true;

            TranslationCorrectionApplyResult applyResult;

            try
            {
                applyResult = plan.Apply(preview.SelectedChanges);
            }
            finally
            {
                if (session is not null)
                    session.HistoryChangeInProgress = false;
            }

            if (applyResult.AppliedChanges.Count > 0)
            {
                state.BulkUndo.Push(
                    new BulkHistoryOperation(
                        applyResult.AppliedChanges,
                        normalUndoBaseline));
                state.BulkRedo.Clear();
            }

            translationBox
                .GetBindingExpression(TextBox.TextProperty)?
                .UpdateTarget();

            AppDialog.Show(
                applyResult.SkippedEntries == 0
                    ? $"Исправлено строк: {applyResult.AppliedEntries:N0}.\n" +
                      $"Неоднозначных случаев оставлено для QA: {plan.ReviewItems.Count:N0}.\n\n" +
                      "Всю операцию можно отменить одним Ctrl+Z."
                    : $"Исправлено строк: {applyResult.AppliedEntries:N0}. " +
                      $"Пропущено изменённых во время предпросмотра: {applyResult.SkippedEntries:N0}.\n" +
                      $"Неоднозначных случаев: {plan.ReviewItems.Count:N0}.",
                "Массовое автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
        }
        finally
        {
            viewModel.StatusFilter = previousStatusFilter;
            viewModel.SearchText = previousSearchText;
            Mouse.OverrideCursor = null;
        }
    }

    private static void Undo(
        MainWindow window,
        TextBox translationBox,
        MainViewModel viewModel,
        EditorState state)
    {
        CommitTranslation(translationBox);

        var session = GetActiveSession(viewModel);
        var operation = state.BulkUndo.Count > 0
            ? state.BulkUndo.Peek()
            : null;

        if (operation is null ||
            session is null ||
            session.UndoStack.Count > operation.NormalUndoBaseline)
        {
            if (viewModel.UndoCommand.CanExecute(null))
                viewModel.UndoCommand.Execute(null);
            return;
        }

        state.BulkUndo.Pop();
        session.HistoryChangeInProgress = true;

        try
        {
            foreach (var change in operation.Changes.Reverse())
            {
                if (string.Equals(
                        change.Entry.Translation,
                        change.After,
                        StringComparison.Ordinal))
                {
                    change.Entry.Translation = change.Before;
                }
            }
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }

        state.BulkRedo.Push(operation);
        RefreshAfterHistory(window, translationBox, viewModel);
    }

    private static void Redo(
        MainWindow window,
        TextBox translationBox,
        MainViewModel viewModel,
        EditorState state)
    {
        CommitTranslation(translationBox);

        var session = GetActiveSession(viewModel);

        if (state.BulkRedo.Count == 0 || session is null)
        {
            if (viewModel.RedoCommand.CanExecute(null))
                viewModel.RedoCommand.Execute(null);
            return;
        }

        var operation = state.BulkRedo.Pop();
        session.HistoryChangeInProgress = true;

        try
        {
            foreach (var change in operation.Changes)
            {
                if (string.Equals(
                        change.Entry.Translation,
                        change.Before,
                        StringComparison.Ordinal))
                {
                    change.Entry.Translation = change.After;
                }
            }
        }
        finally
        {
            session.HistoryChangeInProgress = false;
        }

        state.BulkUndo.Push(operation);
        RefreshAfterHistory(window, translationBox, viewModel);
    }

    private static void RefreshAfterHistory(
        MainWindow window,
        TextBox translationBox,
        MainViewModel viewModel)
    {
        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateTarget();

        viewModel.EntriesView.Refresh();

        if (viewModel.SelectedEntry is not null &&
            window.FindName("EntriesGrid") is DataGrid entriesGrid)
        {
            entriesGrid.UpdateLayout();
            entriesGrid.ScrollIntoView(viewModel.SelectedEntry);
        }
    }

    private static DocumentSession? GetActiveSession(MainViewModel viewModel)
        => typeof(MainViewModel)
            .GetField(
                "_activeSession",
                BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(viewModel) as DocumentSession;

    private static void CommitTranslation(TextBox translationBox)
    {
        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateSource();
    }

    private static bool IsInside(
        DependencyObject source,
        DependencyObject ancestor)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        try
        {
            var visualParent = VisualTreeHelper.GetParent(child);

            if (visualParent is not null)
                return visualParent;
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private sealed class EditorState
    {
        public Stack<BulkHistoryOperation> BulkUndo { get; } = new();
        public Stack<BulkHistoryOperation> BulkRedo { get; } = new();
    }

    private sealed record BulkHistoryOperation(
        IReadOnlyList<TranslationCorrectionChange> Changes,
        int NormalUndoBaseline);

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
