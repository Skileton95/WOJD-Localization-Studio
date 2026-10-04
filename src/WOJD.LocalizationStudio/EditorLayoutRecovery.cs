using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

/// <summary>
/// Keeps the lower editor compact. Feature modules may add commands and menus,
/// but they must not consume the translation editing area.
/// </summary>
internal static class EditorLayoutRecovery
{
    private static readonly ConditionalWeakTable<MainWindow, object> Installed = new();

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

        Installed.Add(window, new object());

        // Other editor modules install at ContextIdle/ApplicationIdle.
        // Run last and normalize the final visual tree.
        window.Dispatcher.BeginInvoke(
            () => Repair(window),
            DispatcherPriority.SystemIdle);
    }

    private static void Repair(MainWindow window)
    {
        if (window.DataContext is not MainViewModel viewModel ||
            window.FindName("TranslationBox") is not TextBox translationBox ||
            window.FindName("EntriesGrid") is not DataGrid entriesGrid ||
            translationBox.Parent is not Border editorBorder ||
            editorBorder.Parent is not Grid editorGrid)
        {
            return;
        }

        var footer = editorGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(x => Grid.GetColumn(x) == 2 && Grid.GetRow(x) == 2);

        if (footer is null)
            return;

        // ReviewWorkflowEnhancements used a third footer row. It made the fixed
        // 280px editor unusable on ordinary window widths. Review controls remain
        // available through the context menu below.
        foreach (var child in footer.Children
                     .Cast<UIElement>()
                     .Where(x => Grid.GetRow(x) >= 2)
                     .ToList())
        {
            footer.Children.Remove(child);
        }

        while (footer.RowDefinitions.Count > 2)
            footer.RowDefinitions.RemoveAt(footer.RowDefinitions.Count - 1);

        while (footer.RowDefinitions.Count < 2)
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        footer.RowDefinitions[0].Height = GridLength.Auto;
        footer.RowDefinitions[1].Height = GridLength.Auto;

        // History and snapshots stay in the application menus; do not duplicate
        // them in the narrow translation footer.
        foreach (var button in footer.Children
                     .OfType<Button>()
                     .Where(x =>
                     {
                         var text = x.Content?.ToString();
                         return string.Equals(text, "История", StringComparison.Ordinal) ||
                                string.Equals(text, "Снимки", StringComparison.Ordinal);
                     })
                     .ToList())
        {
            footer.Children.Remove(button);
        }

        NormalizeQaPanel(footer);
        InstallCompactReviewCommands(window, viewModel, entriesGrid, translationBox);
    }

    private static void NormalizeQaPanel(Grid footer)
    {
        var qaBorder = footer.Children
            .OfType<Border>()
            .FirstOrDefault(x => Grid.GetRow(x) == 0 && x.Child is Grid);

        if (qaBorder?.Child is not Grid qaGrid)
            return;

        while (qaGrid.RowDefinitions.Count < 2)
            qaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        qaGrid.RowDefinitions[0].Height = GridLength.Auto;
        qaGrid.RowDefinitions[1].Height = GridLength.Auto;

        var summary = qaGrid.Children.OfType<TextBlock>().FirstOrDefault();
        if (summary is not null)
        {
            Grid.SetRow(summary, 0);
            Grid.SetColumn(summary, 0);
            Grid.SetColumnSpan(summary, Math.Max(1, qaGrid.ColumnDefinitions.Count));
            summary.TextWrapping = TextWrapping.NoWrap;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.Margin = new Thickness(0, 0, 0, 6);
            summary.MaxHeight = 22;
        }

        // Put all QA actions on their own row. They no longer compete with the
        // status text for horizontal space.
        foreach (var child in qaGrid.Children.Cast<UIElement>().Where(x => !ReferenceEquals(x, summary)))
            Grid.SetRow(child, 1);

        qaBorder.Padding = new Thickness(9, 6, 8, 6);
        qaBorder.Margin = new Thickness(0, 0, 0, 6);
    }

    private static void InstallCompactReviewCommands(
        MainWindow window,
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox)
    {
        var menu = entriesGrid.ContextMenu ?? new ContextMenu();
        entriesGrid.ContextMenu = menu;

        if (menu.Items.OfType<MenuItem>().Any(x =>
                string.Equals(x.Header?.ToString(), "✓ Проверено и дальше", StringComparison.Ordinal)))
        {
            return;
        }

        menu.Items.Add(new Separator());

        var reviewedAndNext = new MenuItem { Header = "✓ Проверено и дальше" };
        reviewedAndNext.Click += (_, _) =>
        {
            Commit(translationBox);
            SetCurrentStatus(viewModel, ReviewState.Reviewed);
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        };
        menu.Items.Add(reviewedAndNext);

        var fixAndNext = new MenuItem { Header = "⚠ Требует правки и дальше" };
        fixAndNext.Click += (_, _) =>
        {
            Commit(translationBox);
            SetCurrentStatus(viewModel, ReviewState.NeedsFix);
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        };
        menu.Items.Add(fixAndNext);

        var next = new MenuItem { Header = "→ Следующая непроверенная" };
        next.Click += (_, _) =>
        {
            Commit(translationBox);
            NavigateNextUnreviewed(viewModel, entriesGrid, translationBox);
        };
        menu.Items.Add(next);
    }

    private static void SetCurrentStatus(MainViewModel viewModel, ReviewState state)
    {
        if (viewModel.ActiveDocument is null || viewModel.SelectedEntry is null)
            return;

        ReviewWorkflowService.SetStatus(
            viewModel.ActiveDocument.FilePath,
            viewModel.SelectedEntry,
            state);
    }

    private static void NavigateNextUnreviewed(
        MainViewModel viewModel,
        DataGrid entriesGrid,
        TextBox translationBox)
    {
        var document = viewModel.ActiveDocument;
        if (document is null || document.Entries.Count == 0)
            return;

        var start = viewModel.SelectedEntry is null
            ? -1
            : document.Entries.IndexOf(viewModel.SelectedEntry);

        LocalizationEntry? target = null;

        for (var offset = 1; offset <= document.Entries.Count; offset++)
        {
            var index = (start + offset) % document.Entries.Count;
            var candidate = document.Entries[index];

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
        entriesGrid.SelectedItem = target;
        entriesGrid.UpdateLayout();
        entriesGrid.ScrollIntoView(target);
        translationBox.Focus();
    }

    private static void Commit(TextBox translationBox)
        => translationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
}
