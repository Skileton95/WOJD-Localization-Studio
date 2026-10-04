using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

public partial class MainWindow
{
    internal void ApplyEditorSettingsShell()
    {
        var settings = EditorSettingsService.Load();

        TranslationBox.FontSize = settings.TranslationFontSize;
        TranslationBox.TextWrapping = settings.WordWrap
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;

        StructureQaBorder.Visibility = settings.ShowQaPanel
            ? Visibility.Visible
            : Visibility.Collapsed;

        OriginalEditorColumn.Width = settings.ShowOriginalPane
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        NamespaceColumn.Width = new DataGridLength(settings.NamespaceColumnWidth);
        KeyColumn.Width = new DataGridLength(settings.KeyColumnWidth);
        StatusColumn.Width = new DataGridLength(settings.StatusColumnWidth);

        if (settings.EditorCollapsed)
        {
            EditorRow.Height = new GridLength(0);
            EditorSplitterRow.Height = new GridLength(0);
            ToggleEditorButton.Content = "Развернуть редактор";
        }
        else
        {
            EditorRow.Height = new GridLength(settings.EditorHeight);
            EditorSplitterRow.Height = new GridLength(6);
            ToggleEditorButton.Content = "Свернуть редактор";
        }
    }

    internal void SaveEditorSettingsShell()
    {
        var settings = EditorSettingsService.Current;

        if (EditorRow.Height.Value > 0)
            settings.EditorHeight = Math.Max(180, EditorRow.ActualHeight);

        settings.EditorCollapsed = EditorRow.Height.Value <= 0;
        settings.TranslationFontSize = TranslationBox.FontSize;
        settings.WordWrap = TranslationBox.TextWrapping != TextWrapping.NoWrap;
        settings.ShowQaPanel = StructureQaBorder.Visibility == Visibility.Visible;
        settings.ShowOriginalPane = OriginalEditorColumn.Width.Value > 0;
        settings.NamespaceColumnWidth = NamespaceColumn.ActualWidth > 0
            ? NamespaceColumn.ActualWidth
            : settings.NamespaceColumnWidth;
        settings.KeyColumnWidth = KeyColumn.ActualWidth > 0
            ? KeyColumn.ActualWidth
            : settings.KeyColumnWidth;
        settings.StatusColumnWidth = StatusColumn.ActualWidth > 0
            ? StatusColumn.ActualWidth
            : settings.StatusColumnWidth;

        EditorSettingsService.Save(settings);
    }

    private void EditorSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (EditorRow.ActualHeight >= 180)
        {
            var settings = EditorSettingsService.Current;
            settings.EditorHeight = EditorRow.ActualHeight;
            settings.EditorCollapsed = false;
            EditorSettingsService.Save(settings);
        }
    }

    private void ToggleEditor_Click(object sender, RoutedEventArgs e)
    {
        var settings = EditorSettingsService.Current;
        var collapsed = EditorRow.Height.Value <= 0;

        if (collapsed)
        {
            EditorRow.Height = new GridLength(Math.Max(180, settings.EditorHeight));
            EditorSplitterRow.Height = new GridLength(6);
            ToggleEditorButton.Content = "Свернуть редактор";
            settings.EditorCollapsed = false;
        }
        else
        {
            if (EditorRow.ActualHeight >= 180)
                settings.EditorHeight = EditorRow.ActualHeight;

            EditorRow.Height = new GridLength(0);
            EditorSplitterRow.Height = new GridLength(0);
            ToggleEditorButton.Content = "Развернуть редактор";
            settings.EditorCollapsed = true;
        }

        EditorSettingsService.Save(settings);
    }

    private void StructureDiff_Click(object sender, RoutedEventArgs e)
        => ShowStructureDiff();

    private void StructureDiffContext_Click(object sender, RoutedEventArgs e)
        => ShowStructureDiff();

    private void ShowStructureDiff()
    {
        if (_viewModel.SelectedEntry is not LocalizationEntry entry)
            return;

        new StructureDiffWindow(entry) { Owner = this }.ShowDialog();
    }

    private void TranslationMemory_Click(object sender, RoutedEventArgs e)
        => ShowTranslationMemory();

    private void TranslationMemoryContext_Click(object sender, RoutedEventArgs e)
        => ShowTranslationMemory();

    private void ShowTranslationMemory()
    {
        if (_viewModel.ActiveDocument is not LocalizationDocument document ||
            _viewModel.SelectedEntry is not LocalizationEntry entry)
        {
            AppDialog.Show(
                "Выберите строку в открытом файле.",
                "Translation Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        var dialog = new TranslationMemoryWindow(document, entry)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.AppliedTranslation is null)
            return;

        var before = entry.Translation;
        if (string.Equals(before, dialog.AppliedTranslation, StringComparison.Ordinal))
            return;

        using (EntryHistoryService.BeginOperation("Translation Memory"))
        {
            EntryHistoryService.Record(entry, before, dialog.AppliedTranslation, "Translation Memory");
            entry.Translation = dialog.AppliedTranslation;
        }

        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void Consistency_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is not LocalizationDocument document)
            return;

        var dialog = new ConsistencyWindow(document)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.NavigateToEntry is null)
            return;

        _viewModel.SelectedEntry = dialog.NavigateToEntry;
        _viewModel.EntriesView.MoveCurrentTo(dialog.NavigateToEntry);
        EntriesGrid.SelectedItem = dialog.NavigateToEntry;
        EntriesGrid.UpdateLayout();
        EntriesGrid.ScrollIntoView(dialog.NavigateToEntry);
    }

    private void Glossary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GlossaryWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        GlossaryService.Reload();

        if (_viewModel.ActiveDocument is not LocalizationDocument document)
            return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (var entry in document.Entries)
                entry.RefreshValidation();

            _viewModel.EntriesView.Refresh();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ProjectHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is not LocalizationDocument document)
            return;

        new ProjectHistoryWindow(document.FilePath) { Owner = this }.ShowDialog();
    }

    private void EntryHistoryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedEntry is not LocalizationEntry entry)
            return;

        var dialog = new EntryHistoryWindow(entry)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _viewModel.EntriesView.Refresh();
        }
    }

    private void SnapshotsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is not LocalizationDocument document)
            return;

        TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var dialog = new SnapshotManagerWindow(document)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Restored)
        {
            TranslationBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            _viewModel.EntriesView.Refresh();
        }
    }

    private void QaProfilesMenu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new QaProfilesWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            _viewModel.ActiveDocument is not LocalizationDocument document)
        {
            return;
        }

        foreach (var entry in document.Entries)
            entry.RefreshValidation();

        _viewModel.EntriesView.Refresh();
    }

    private void EditorSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorSettingsShell();

        var dialog = new EditorSettingsWindow(EditorSettingsService.Current)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
            ApplyEditorSettingsShell();
    }
}

internal static class EditorFeatureShellBootstrap
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
        window.ApplyEditorSettingsShell();
        window.Closed += (_, _) => window.SaveEditorSettingsShell();
    }
}
