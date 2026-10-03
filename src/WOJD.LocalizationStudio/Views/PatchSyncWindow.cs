using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;

public sealed class PatchSyncWindow : WorkflowWindow
{
    public PatchSyncWindow(MainViewModel vm) : base("Синхронизация патча — несколько файлов")
    {
        var rows = new List<PatchSyncRow>();
        var grid = Table(rows, ("Категория", "Kind"), ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Ключ", "Key"),
            ("Старый оригинал", "OldSource"), ("Новый оригинал", "NewSource"), ("Старый перевод", "OldTranslation"), ("Текущий перевод", "NewTranslation"));
        grid.IsReadOnly = false; foreach (var col in grid.Columns) col.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        var overwrite = new CheckBox { Content = "Разрешить замену существующих переводов", Margin = new Thickness(0,8,8,8) };
        var category = new ComboBox { ItemsSource = new[] { "Все", "Новая", "Удалена", "Оригинал изменён", "Перевод изменён", "Можно перенести", "Коллизия", "Совпадает" }, SelectedIndex = 0, Width = 180 };
        void Refresh() { grid.ItemsSource = category.SelectedIndex == 0 ? rows : rows.Where(x => x.Kind == (string)category.SelectedItem).ToList(); }
        category.SelectionChanged += (_, _) => Refresh();
        var toolbar = new WrapPanel();
        toolbar.Children.Add(ActionButton("Выбрать старые файлы…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "NDJSON/JSONL|*.ndjson;*.jsonl" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                await vm.EnsureProjectLoadedAsync();
                var old = new List<LocalizationDocument>();
                foreach (var file in dialog.FileNames) old.Add(await new NdjsonLocalizationAdapter().LoadAsync(file));
                rows = PatchSyncService.Preview(vm.OpenDocuments, old); Refresh();
                Status.Text = string.Join(" · ", rows.GroupBy(x => x.Kind).Select(g => $"{g.Key}: {g.Count()}"));
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        toolbar.Children.Add(ActionButton("Перенести отмеченные", (_, _) =>
        {
            try
            {
                grid.CommitEdit(); grid.CommitEdit();
                var changes = PatchSyncService.Transfers(rows, overwrite.IsChecked == true).ToList();
                vm.ApplyBatch(changes, "Перенос переводов"); Status.Text = $"Перенесено: {changes.Count}. Коллизии и изменённый source заблокированы. Доступен общий Undo.";
                foreach (var row in rows) row.Include = false;
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        toolbar.Children.Add(ActionButton("Экспорт отчёта…", async (_, _) =>
        {
            var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "patch-sync.json" };
            if (save.ShowDialog(this) != true) return;
            try { await File.WriteAllTextAsync(save.FileName, JsonSerializer.Serialize(rows.Select(x => new { x.Kind, x.FilePath, x.Namespace, x.Key, x.OldSource, x.NewSource, x.OldTranslation, x.NewTranslation }), new JsonSerializerOptions { WriteIndented = true })); Status.Text = "Отчёт сохранён."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(toolbar); AddToolbar(overwrite); AddToolbar(category); Body.Children.Add(grid);
        Status.Text = "Текущая версия — файлы проекта. Выберите файлы предыдущей версии для сравнения по точным Namespace + Key + source.";
    }
}