using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class StructureProtectionEnhancements
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

        window.Dispatcher.BeginInvoke(
            () => Install(window),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(MainWindow window)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox ||
            translationBox.Parent is not Border editorBorder ||
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

        var qaBorder =
            footer.Children
                .OfType<Border>()
                .FirstOrDefault(x =>
                    Grid.GetRow(x) == 0 &&
                    x.Child is Grid);

        if (qaBorder?.Child is not Grid qaGrid)
            return;

        var qaText = qaGrid.Children
            .OfType<TextBlock>()
            .FirstOrDefault();

        if (qaText is null)
            return;

        var restoreButton = CreateButton(
            window,
            "↺ Структура",
            "Восстановить только теги и плейсхолдеры из оригинала. Русский текст не изменяется.");
        restoreButton.Margin = new Thickness(7, 0, 7, 0);

        var protectionToggle = new CheckBox
        {
            Content = "Защита",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip =
                "Не позволяет случайно ухудшить структуру тегов и плейсхолдеров. " +
                "При вставке опасного текста можно явно подтвердить действие."
        };

        qaGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        qaGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(restoreButton, 3);
        qaGrid.Children.Add(restoreButton);

        Grid.SetColumn(protectionToggle, 4);
        qaGrid.Children.Add(protectionToggle);

        var state = new ProtectionState();

        void RefreshStatus()
        {
            var entry = viewModel.SelectedEntry;

            if (entry is null)
            {
                qaText.Text = "Структура: выберите строку для проверки.";
                qaText.ToolTip = null;
                restoreButton.IsEnabled = false;
                protectionToggle.IsEnabled = false;
                SetOkAppearance(qaBorder, qaText);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.Original))
            {
                qaText.Text = "— Исходный текст отсутствует — структура не проверяется.";
                qaText.ToolTip =
                    "Защита и восстановление структуры отключены: в строке нет Original.";
                restoreButton.IsEnabled = false;
                protectionToggle.IsEnabled = false;
                SetUnavailableAppearance(qaBorder, qaText);
                return;
            }

            restoreButton.IsEnabled = true;
            protectionToggle.IsEnabled = true;

            var status = StructureProtectionService.Analyze(
                entry.Original,
                translationBox.Text);

            qaText.Text = status.CompactSummary;

            var details = new List<string>();

            if (status.StructuralResult.HasIssues)
                details.Add(status.StructuralResult.Summary);

            if (status.HasNewLineIssues)
            {
                details.Add(
                    $"Переносы — оригинал: {status.SourceNewLineCount}, " +
                    $"перевод: {status.TargetNewLineCount}");
            }

            qaText.ToolTip = details.Count == 0
                ? "Структура совпадает с оригиналом."
                : string.Join("\n", details);

            if (status.HasTagIssues ||
                status.HasPlaceholderIssues ||
                status.HasNewLineIssues)
            {
                SetErrorAppearance(qaBorder, qaText);
            }
            else
            {
                SetOkAppearance(qaBorder, qaText);
            }
        }

        void ShowBlockedMessage(string message)
        {
            qaText.Text = "🔒 " + message;
            qaText.ToolTip = message;
            SetProtectionAppearance(qaBorder, qaText);
            System.Media.SystemSounds.Beep.Play();

            state.MessageTimer?.Stop();
            state.MessageTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1300)
            };
            state.MessageTimer.Tick += (_, _) =>
            {
                state.MessageTimer?.Stop();
                RefreshStatus();
            };
            state.MessageTimer.Start();
        }

        bool ProtectionEnabled()
            => protectionToggle.IsChecked == true &&
               protectionToggle.IsEnabled &&
               viewModel.SelectedEntry is not null &&
               !string.IsNullOrWhiteSpace(viewModel.SelectedEntry.Original);

        bool ShouldBlock(string proposedText)
        {
            var entry = viewModel.SelectedEntry;

            if (!ProtectionEnabled() || entry is null)
                return false;

            return StructureProtectionService.WouldWorsenStructure(
                entry.Original,
                translationBox.Text,
                proposedText);
        }

        restoreButton.Click += (_, _) =>
        {
            var entry = viewModel.SelectedEntry;
            if (entry is null || string.IsNullOrWhiteSpace(entry.Original))
                return;

            var result = StructureProtectionService.RestoreStructure(
                entry.Original,
                translationBox.Text);

            if (!result.Changed)
            {
                AppDialog.Show(
                    result.Message,
                    "Восстановление структуры",
                    MessageBoxButton.OK,
                    result.CanRestore
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning,
                    window);
                return;
            }

            var caret = translationBox.CaretIndex;
            translationBox.Text = result.Text;
            translationBox.CaretIndex = Math.Min(caret, translationBox.Text.Length);
            translationBox
                .GetBindingExpression(TextBox.TextProperty)?
                .UpdateSource();

            RefreshStatus();
        };

        translationBox.PreviewTextInput += (_, args) =>
        {
            if (!ProtectionEnabled() || string.IsNullOrEmpty(args.Text))
                return;

            var proposed = ReplaceSelection(
                translationBox,
                args.Text);

            if (!ShouldBlock(proposed))
                return;

            args.Handled = true;
            ShowBlockedMessage(
                "Изменение затрагивает защищённый тег или плейсхолдер.");
        };

        translationBox.PreviewKeyDown += (_, args) =>
        {
            if (!ProtectionEnabled())
                return;

            string? proposed = null;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (args.Key == Key.Back)
            {
                proposed = ctrl
                    ? DeletePreviousWord(translationBox)
                    : DeleteBackward(translationBox);
            }
            else if (args.Key == Key.Delete)
            {
                proposed = ctrl
                    ? DeleteNextWord(translationBox)
                    : DeleteForward(translationBox);
            }
            else if ((ctrl && args.Key == Key.X) ||
                     (shift && args.Key == Key.Delete))
            {
                proposed = ReplaceSelection(
                    translationBox,
                    string.Empty);
            }

            if (proposed is null || !ShouldBlock(proposed))
                return;

            args.Handled = true;
            ShowBlockedMessage(
                "Удаление защищённого тега или плейсхолдера отменено.");
        };

        CommandManager.AddPreviewExecutedHandler(
            translationBox,
            (_, args) =>
            {
                if (!ProtectionEnabled())
                    return;

                if (ReferenceEquals(args.Command, ApplicationCommands.Cut))
                {
                    var proposed = ReplaceSelection(
                        translationBox,
                        string.Empty);

                    if (ShouldBlock(proposed))
                    {
                        args.Handled = true;
                        ShowBlockedMessage(
                            "Вырезание защищённого тега или плейсхолдера отменено.");
                    }
                }
            });

        DataObject.AddPastingHandler(
            translationBox,
            (_, args) =>
            {
                if (!ProtectionEnabled())
                    return;

                var pasted = GetPastedText(args.DataObject);
                if (pasted is null)
                    return;

                var proposed = ReplaceSelection(
                    translationBox,
                    pasted);

                if (!ShouldBlock(proposed))
                    return;

                var entry = viewModel.SelectedEntry;
                var after = entry is null
                    ? null
                    : StructureProtectionService.Analyze(
                        entry.Original,
                        proposed);

                var message =
                    "После вставки структура тегов или плейсхолдеров станет хуже." +
                    (after is null
                        ? string.Empty
                        : "\n\n" + after.StructuralResult.Summary) +
                    "\n\nВставить всё равно?";

                var answer = AppDialog.Show(
                    message,
                    "Защита структуры",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    window);

                if (answer != MessageBoxResult.Yes)
                {
                    args.CancelCommand();
                    ShowBlockedMessage("Опасная вставка отменена.");
                }
            });

        translationBox.TextChanged += (_, _) =>
            window.Dispatcher.BeginInvoke(
                RefreshStatus,
                DispatcherPriority.Background);

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedEntry))
            {
                window.Dispatcher.BeginInvoke(
                    RefreshStatus,
                    DispatcherPriority.Background);
            }
        };

        protectionToggle.Checked += (_, _) => RefreshStatus();
        protectionToggle.Unchecked += (_, _) => RefreshStatus();

        RefreshStatus();
    }

    private static Button CreateButton(
        MainWindow window,
        string content,
        string toolTip)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 5, 10, 5),
            ToolTip = toolTip,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (window.TryFindResource("SecondaryButton") is Style style)
            button.Style = style;

        return button;
    }

    private static string ReplaceSelection(
        TextBox box,
        string replacement)
    {
        var text = box.Text ?? string.Empty;
        var start = Math.Clamp(box.SelectionStart, 0, text.Length);
        var length = Math.Clamp(box.SelectionLength, 0, text.Length - start);

        return text
            .Remove(start, length)
            .Insert(start, replacement);
    }

    private static string DeleteBackward(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);

        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        if (caret == 0)
            return text;

        return text.Remove(caret - 1, 1);
    }

    private static string DeleteForward(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);

        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        if (caret >= text.Length)
            return text;

        return text.Remove(caret, 1);
    }

    private static string DeletePreviousWord(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);

        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var start = caret;

        while (start > 0 && char.IsWhiteSpace(text[start - 1]))
            start--;

        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            start--;

        return text.Remove(start, caret - start);
    }

    private static string DeleteNextWord(TextBox box)
    {
        if (box.SelectionLength > 0)
            return ReplaceSelection(box, string.Empty);

        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var end = caret;

        while (end < text.Length && char.IsWhiteSpace(text[end]))
            end++;

        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        return text.Remove(caret, end - caret);
    }

    private static string? GetPastedText(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.UnicodeText))
            return data.GetData(DataFormats.UnicodeText) as string;

        if (data.GetDataPresent(DataFormats.Text))
            return data.GetData(DataFormats.Text) as string;

        return null;
    }

    private static void SetErrorAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(255, 244, 244));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(242, 184, 184));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(185, 48, 48));
        text.FontWeight = FontWeights.SemiBold;
    }

    private static void SetOkAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(247, 250, 252));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(226, 232, 240));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(94, 104, 119));
        text.FontWeight = FontWeights.Normal;
    }

    private static void SetUnavailableAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(255, 249, 235));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(238, 203, 126));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(145, 102, 17));
        text.FontWeight = FontWeights.SemiBold;
    }

    private static void SetProtectionAppearance(
        Border border,
        TextBlock text)
    {
        border.Background =
            new SolidColorBrush(Color.FromRgb(255, 249, 235));
        border.BorderBrush =
            new SolidColorBrush(Color.FromRgb(240, 203, 122));
        text.Foreground =
            new SolidColorBrush(Color.FromRgb(154, 102, 0));
        text.FontWeight = FontWeights.SemiBold;
    }

    private sealed class ProtectionState
    {
        public DispatcherTimer? MessageTimer { get; set; }
    }
}
