using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ImportWorkflowWindow : WorkflowWindow
{
    public ImportWorkflowWindow(MainViewModel vm) : base("Импорт и синхронизация WOJD")
    {
        var rows = new List<ImportRow>();
        var role = new ComboBox { ItemsSource = new[] { "RU", "CN", "EN" }, SelectedIndex = 0, Width = 70 };
        var overwrite = new CheckBox { Content = "Разрешить замену заполненных значений", Margin = new Thickness(8) };
        var grid = Table(rows, ("Результат", "Kind"), ("Namespace", "Namespace"), ("Key", "Key"), ("Было", "Before"), ("Будет", "After"), ("Целевой файл", "FilePath"));
        grid.IsReadOnly = false; foreach (var column in grid.Columns) column.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        void Invalidate() { rows.Clear(); grid.ItemsSource = null; Status.Text = "Настройки изменены. Повторите предварительный просмотр."; }
        role.SelectionChanged += (_, _) => Invalidate(); overwrite.Checked += (_, _) => Invalidate(); overwrite.Unchecked += (_, _) => Invalidate();
        var bar = new WrapPanel(); bar.Children.Add(role); bar.Children.Add(overwrite);
        bar.Children.Add(ActionButton("Выбрать входящие файлы…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "NDJSON/JSONL|*.ndjson;*.jsonl" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var targets = await vm.WojdTargetsAsync();
                var incoming = new List<LocalizationDocument>(); foreach (var path in dialog.FileNames) incoming.Add(await new NdjsonLocalizationAdapter().LoadAsync(path));
                rows = ProjectImportService.Preview(targets, incoming, role.Text, overwrite.IsChecked == true); grid.ItemsSource = rows;
                Status.Text = string.Join(" · ", rows.GroupBy(r => r.Kind).Select(g => $"{g.Key}: {g.Count()}"));
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Применить выбранные", (_, _) =>
        {
            try { if (vm.IsBusy) return; grid.CommitEdit(); grid.CommitEdit(); var changes = ProjectImportService.Changes(rows).ToList(); vm.ApplyBatch(changes, "Импорт " + role.Text); foreach (var row in rows) row.Include = false; grid.Items.Refresh(); Status.Text = $"Изменено: {changes.Count}. Доступен общий Undo; сохраните файлы после проверки."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Экспорт новых строк…", async (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "NDJSON|*.ndjson", FileName = "new-entries.ndjson" };
            if (dialog.ShowDialog(this) != true) return;
            try { if (File.Exists(dialog.FileName)) throw new IOException("Выберите новый файл."); var lines = rows.Where(r => r.Kind == "Новая").Select(r => role.Text == "RU" ? r.Incoming.Entry.RawLine : JsonSerializer.Serialize(new { @namespace = r.Namespace, key = r.Key, source = r.After, translation = "" })); await File.WriteAllLinesAsync(dialog.FileName, lines); Status.Text = "Новые строки сохранены отдельно."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Отчёт…", async (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "import-report.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { if (File.Exists(dialog.FileName)) throw new IOException("Выберите новый файл отчёта."); await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(rows.Select(r => new { r.Kind, r.Namespace, r.Key, r.FilePath, r.Before, r.After }), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(grid);
        Status.Text = "RU импортирует перевод только при совпадении source. CN/EN заполняет оригинал без автоматической смены уже переведённых строк. Цель — RU проекта, либо открытые файлы.";
    }
}
