using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Views;

public partial class ShellWindow
{
    private bool _parityCommandsInstalled;

    private void InstallLegacyParityCommands()
    {
        if (_parityCommandsInstalled || ToolsButton.ContextMenu is null)
            return;

        var menu = ToolsButton.ContextMenu;
        menu.Items.Add(new Separator());

        var replace = new MenuItem { Header = "Найти и заменить…", InputGestureText = "Ctrl+H" };
        replace.Click += (_, _) => ShowFindReplace();
        menu.Items.Add(replace);

        var jump = new MenuItem { Header = "Перейти к строке или ключу…", InputGestureText = "Ctrl+G" };
        jump.Click += (_, _) => ShowJumpToEntry();
        menu.Items.Add(jump);

        menu.Items.Add(new Separator());
        var exportCurrent = new MenuItem { Header = "Экспортировать текущую строку…" };
        exportCurrent.Click += async (_, _) =>
        {
            TranslationPage.CommitTranslation();
            if (_viewModel.SelectedEntry is LocalizationEntry entry)
                await _viewModel.ExportEntriesAsync([entry], "current");
        };
        menu.Items.Add(exportCurrent);

        var exportUntranslated = new MenuItem { Header = "Экспортировать непереведённые…" };
        exportUntranslated.Click += async (_, _) =>
        {
            TranslationPage.CommitTranslation();
            await _viewModel.ExportEntriesAsync(_viewModel.GetUntranslatedEntries(), "untranslated");
        };
        menu.Items.Add(exportUntranslated);

        var exportQa = new MenuItem { Header = "Экспортировать QA-проблемы…" };
        exportQa.Click += async (_, _) =>
        {
            TranslationPage.CommitTranslation();
            await _viewModel.ExportEntriesAsync(_viewModel.GetErrorEntries(), "qa-errors");
        };
        menu.Items.Add(exportQa);

        PreviewKeyDown += ShellWindow_ParityPreviewKeyDown;
        _parityCommandsInstalled = true;
    }

    private void ShellWindow_ParityPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl)
            return;

        if (!shift && e.Key == Key.H)
        {
            ShowFindReplace();
            e.Handled = true;
            return;
        }

        if (!shift && e.Key == Key.G)
        {
            ShowJumpToEntry();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
            return;

        if (!shift && e.Key == Key.Z)
        {
            TranslationPage.CommitTranslation();
            if (_viewModel.UndoCommand.CanExecute(null))
                _viewModel.UndoCommand.Execute(null);
            TranslationPage.Attach(_viewModel);
            TranslationPage.ScrollToSelected();
            e.Handled = true;
        }
        else if (!shift && e.Key == Key.Y)
        {
            TranslationPage.CommitTranslation();
            if (_viewModel.RedoCommand.CanExecute(null))
                _viewModel.RedoCommand.Execute(null);
            TranslationPage.Attach(_viewModel);
            TranslationPage.ScrollToSelected();
            e.Handled = true;
        }
    }

    private void ShowFindReplace()
    {
        TranslationPage.CommitTranslation();
        var dialog = new FindReplaceWindow(_viewModel) { Owner = this };
        dialog.ShowDialog();
        TranslationPage.Attach(_viewModel);
        TranslationPage.ScrollToSelected();
    }

    private void ShowJumpToEntry()
    {
        TranslationPage.CommitTranslation();
        var document = _viewModel.ActiveDocument;
        if (document is null)
        {
            AppDialog.Show(
                "Сначала откройте файл.",
                "Переход",
                MessageBoxButton.OK,
                MessageBoxImage.Information,
                this);
            return;
        }

        var dialog = new JumpToEntryWindow(document) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedEntry is null)
            return;

        if (_viewModel.FilterAllCommand.CanExecute(null))
            _viewModel.FilterAllCommand.Execute(null);
        _viewModel.SearchText = string.Empty;
        _viewModel.SelectedEntry = dialog.SelectedEntry;
        _viewModel.EntriesView.MoveCurrentTo(dialog.SelectedEntry);
        ShowTranslation();
        TranslationPage.ScrollToSelected();
    }
}
