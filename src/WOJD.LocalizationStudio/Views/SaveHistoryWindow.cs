using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class SaveHistoryWindow : WorkflowWindow
{
    public SaveHistoryWindow(MainViewModel vm) : base("История сохранений")
    {
        if (vm.ActiveDocument is not { } current) { Status.Text = "Откройте файл."; return; }
        var versions = Table(BackupService.List(current.FilePath), ("Дата UTC", "CreatedUtc"), ("Размер", "Bytes"), ("Копия", "Path"));
        var changes = Table(null!, ("Namespace", "Namespace"), ("Ключ", "Key"), ("До", "OldTranslation"), ("Сейчас", "NewTranslation"), ("Оригинал", "OldOriginal"));
        versions.SelectionChanged += async (_, _) =>
        {
            if (versions.SelectedItem is not BackupVersion version) return;
            try { var old = await new NdjsonLocalizationAdapter().LoadAsync(version.Path); changes.ItemsSource = FileComparisonService.Compare(current, old).Items; }
            catch (Exception e) { Status.Text = e.Message; }
        };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Восстановить перевод выбранной строки", (_, _) =>
        {
            if (changes.SelectedItem is FileComparisonItem { CurrentEntry: { } row, OldEntry: { } old } && row.Original == old.Original)
                vm.ApplyBatch(new[] { (row, EntryField.Translation, row.Translation, old.Translation) });
            else Status.Text = "Строка недоступна или оригинал изменился. Используйте просмотр полной копии.";
        }));
        actions.Children.Add(ActionButton("Вернуть строку к сохранённому", (_, _) => vm.RevertSelectedToSaved()));
        actions.Children.Add(ActionButton("Восстановить файл из копии…", async (_, _) =>
        {
            if (versions.SelectedItem is not BackupVersion version) return;
            if (AppDialog.Show("Восстановить файл на диске? Текущий файл и несохранённый черновик будут сохранены в резервные копии.", "Восстановление файла", MessageBoxButton.YesNo, MessageBoxImage.Warning, this) != MessageBoxResult.Yes) return;
            try { await vm.RestoreBackupAsync(version.Path); Status.Text = "Файл восстановлен; предыдущая версия и черновик сохранены."; Close(); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(actions);
        var split = new Grid(); split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) }); split.RowDefinitions.Add(new RowDefinition());
        split.Children.Add(versions); Grid.SetRow(changes, 1); split.Children.Add(changes); Body.Children.Add(split);
        Status.Text = "Копии создаются перед сохранением. Хранятся последние 50 копий каждого файла; целое восстановление выполняется явно.";
    }
}