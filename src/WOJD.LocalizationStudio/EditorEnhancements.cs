using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class EditorEnhancements
{
    private static readonly ConditionalWeakTable<MainWindow, object>
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

        InstalledWindows.Add(window, new object());

        if (window.FindName("TranslationBox") is not TextBox translationBox)
            return;

        InstallSafeTranslationBinding(
            window,
            translationBox);

        InstallAutoCorrectionControls(
            window,
            translationBox);
    }

    private static void InstallSafeTranslationBinding(
        MainWindow window,
        TextBox translationBox)
    {
        // Раньше UpdateSourceTrigger=PropertyChanged менял модель на каждый
        // символ. При активном фильтре/поиске ViewModel мог сразу обновить
        // CollectionView, из-за чего TextBox терял корректное выделение и
        // позицию каретки во время Backspace/Delete.
        //
        // Теперь ввод является локальным сеансом редактирования и атомарно
        // фиксируется при выходе из поля/смене контекста.
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
            var ctrl =
                (modifiers & ModifierKeys.Control) != 0;

            if (ctrl && args.Key == Key.Enter)
            {
                AutoCorrectCurrent(
                    window,
                    translationBox);
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
        TextBox translationBox)
    {
        if (window.FindName("EntriesGrid") is not DataGrid entriesGrid)
            return;

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

        // Старые навигационные/Apply-кнопки больше не нужны в редакторе.
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
                "Без ИИ: безопасно исправляет пробелы и пунктуацию. Теги и плейсхолдеры не изменяются.");

        autoCorrectButton.Click += (_, _) =>
            AutoCorrectCurrent(
                window,
                translationBox);

        Grid.SetColumn(
            autoCorrectButton,
            1);
        footer.Children.Add(autoCorrectButton);

        var bulkButton =
            CreateButton(
                window,
                "Массово…",
                "SecondaryButton",
                "Автоисправление выделенных строк или всего текущего отфильтрованного списка.");

        bulkButton.Margin =
            new Thickness(8, 0, 8, 0);

        bulkButton.Click += (_, _) =>
            AutoCorrectBulk(
                window,
                translationBox,
                entriesGrid);

        Grid.SetColumn(
            bulkButton,
            2);
        footer.Children.Add(bulkButton);

        var aiButton =
            CreateButton(
                window,
                "ИИ-исправление",
                "SecondaryButton",
                "Интерфейс подготовлен. ИИ-провайдер будет подключён к тому же механизму исправлений позднее.");

        aiButton.IsEnabled = false;

        Grid.SetColumn(
            aiButton,
            3);
        footer.Children.Add(aiButton);
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

        var result =
            TranslationAutoCorrectionService.RuleBased
                .Correct(entry);

        if (!result.HasChanges)
        {
            AppDialog.Show(
                "Безопасных автоисправлений для этой строки не найдено.",
                "Автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        entry.Translation = result.CorrectedText;

        translationBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateTarget();
    }

    private static void AutoCorrectBulk(
        MainWindow window,
        TextBox translationBox,
        DataGrid entriesGrid)
    {
        CommitTranslation(translationBox);

        if (window.DataContext is not MainViewModel viewModel)
            return;

        var selected =
            entriesGrid.SelectedItems
                .OfType<LocalizationEntry>()
                .Distinct()
                .ToList();

        var useSelection =
            selected.Count > 1;

        var candidates =
            useSelection
                ? selected
                : entriesGrid.Items
                    .OfType<LocalizationEntry>()
                    .Distinct()
                    .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(
                "Нет строк для массового автоисправления.",
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
            plan =
                TranslationAutoCorrectionService.BuildPlan(
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
                "В выбранной области безопасных автоисправлений не найдено.",
                "Массовое автоисправление",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                window);
            return;
        }

        var scopeText =
            useSelection
                ? $"выделенные строки: {candidates.Count:N0}"
                : $"текущий список: {candidates.Count:N0}";

        var confirmation =
            AppDialog.Show(
                $"Область: {scopeText}.\n" +
                $"Будет изменено строк: {plan.AffectedEntries:N0}.\n" +
                $"Найдено исправлений: {plan.TotalFixes:N0}.\n\n" +
                "Применить автоисправление?",
                "Массовое автоисправление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                window);

        if (confirmation != MessageBoxResult.Yes)
            return;

        // Во время пакетного применения временно отключаем фильтр статуса
        // и текстовый поиск. Это не даёт CollectionView перестраиваться
        // после каждой отдельной строки. Пользовательские значения затем
        // восстанавливаются.
        var previousStatusFilter =
            viewModel.StatusFilter;
        var previousSearchText =
            viewModel.SearchText;

        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            viewModel.StatusFilter = "Все";
            viewModel.SearchText = string.Empty;

            var applyResult = plan.Apply();

            // Синхронизируем видимый редактор до открытия диалога, иначе
            // Window.Deactivated мог бы зафиксировать в SelectedEntry старый
            // текст из TextBox поверх уже исправленного значения.
            translationBox
                .GetBindingExpression(TextBox.TextProperty)?
                .UpdateTarget();

            AppDialog.Show(
                applyResult.SkippedEntries == 0
                    ? $"Исправлено строк: {applyResult.AppliedEntries:N0}."
                    : $"Исправлено строк: {applyResult.AppliedEntries:N0}. " +
                      $"Пропущено изменённых во время операции: {applyResult.SkippedEntries:N0}.",
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

    private static void CommitTranslation(
        TextBox translationBox)
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

    private static DependencyObject? GetParent(
        DependencyObject child)
    {
        try
        {
            var visualParent =
                VisualTreeHelper.GetParent(child);

            if (visualParent is not null)
                return visualParent;
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(child);
    }
}
