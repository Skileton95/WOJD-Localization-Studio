using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class GlossaryWindow : Window
{
    private readonly GlossaryService _service;
    private readonly ICollectionView _view;
    private GlossaryEntry? _selected;

    public GlossaryWindow(GlossaryService service)
    {
        InitializeComponent();

        _service = service;
        GlossaryGrid.ItemsSource = _service.Entries;

        _view = CollectionViewSource.GetDefaultView(_service.Entries);
        _view.Filter = FilterEntry;

        RefreshSummary();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not GlossaryEntry entry)
            return false;

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(entry.Source, query)
               || Contains(entry.Translation, query)
               || Contains(entry.Note, query);
    }

    private static bool Contains(string value, string query) =>
        (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void GlossaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = GlossaryGrid.SelectedItem as GlossaryEntry;

        if (_selected is null)
        {
            ClearEditor();
            return;
        }

        EditorTitle.Text = "Редактирование термина";
        SourceBox.Text = _selected.Source;
        TranslationBox.Text = _selected.Translation;
        NoteBox.Text = _selected.Note;
        LockedCheckBox.IsChecked = _selected.IsLocked;
        DeleteButton.IsEnabled = true;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        GlossaryGrid.SelectedItem = null;
        ClearEditor();
        SourceBox.Focus();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim();
        var translation = TranslationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translation))
        {
            MessageBox.Show(
                this,
                "Заполните китайский термин и русский перевод.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var duplicate = _service.Entries.FirstOrDefault(entry =>
            !ReferenceEquals(entry, _selected) &&
            string.Equals(entry.Source, source, StringComparison.Ordinal));

        if (duplicate is not null)
        {
            MessageBox.Show(
                this,
                $"Термин «{source}» уже существует в глоссарии.",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_selected is null)
        {
            _selected = new GlossaryEntry();
            _service.Entries.Add(_selected);
        }

        _selected.Source = source;
        _selected.Translation = translation;
        _selected.Note = NoteBox.Text.Trim();
        _selected.IsLocked = LockedCheckBox.IsChecked == true;

        await _service.SaveAsync();

        _view.Refresh();
        GlossaryGrid.SelectedItem = _selected;
        GlossaryGrid.ScrollIntoView(_selected);

        RefreshSummary();
        StatusText.Text = $"Сохранено: {source}";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        var result = MessageBox.Show(
            this,
            $"Удалить термин «{_selected.Source}»?",
            "Глоссарий",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var source = _selected.Source;
        _service.Entries.Remove(_selected);
        _selected = null;

        await _service.SaveAsync();

        ClearEditor();
        RefreshSummary();
        StatusText.Text = $"Удалено: {source}";
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Импорт глоссария"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var mode = MessageBox.Show(
            this,
            "Объединить импортируемые термины с текущим глоссарием?\n\n" +
            "Да — объединить и обновить совпадающие термины.\n" +
            "Нет — полностью заменить текущий глоссарий.\n" +
            "Отмена — ничего не импортировать.",
            "Импорт глоссария",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (mode == MessageBoxResult.Cancel)
            return;

        try
        {
            var changed = await _service.ImportAsync(
                dialog.FileName,
                replace: mode == MessageBoxResult.No);

            _view.Refresh();
            RefreshSummary();
            ClearEditor();

            StatusText.Text = $"Импортировано/обновлено терминов: {changed:N0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось импортировать глоссарий.\n\n{ex.Message}",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "wojd-glossary.json",
            Title = "Экспорт глоссария"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await _service.ExportAsync(dialog.FileName);
            StatusText.Text = $"Экспортировано терминов: {_service.Entries.Count:N0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось экспортировать глоссарий.\n\n{ex.Message}",
                "Глоссарий",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearEditor()
    {
        EditorTitle.Text = "Новый термин";
        SourceBox.Clear();
        TranslationBox.Clear();
        NoteBox.Clear();
        LockedCheckBox.IsChecked = true;
        DeleteButton.IsEnabled = false;
    }

    private void RefreshSummary()
    {
        var locked = _service.Entries.Count(entry => entry.IsLocked);
        GlossarySummaryText.Text =
            $"Терминов: {_service.Entries.Count:N0} • закреплено: {locked:N0}";
    }
}
