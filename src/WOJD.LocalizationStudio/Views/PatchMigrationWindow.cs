using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class PatchMigrationWindow : WorkflowWindow
{
    public PatchMigrationWindow(MainViewModel vm) : base("Миграция переводов между патчами")
    {
        var rows = new List<MigrationRow>();
        var overwrite = new CheckBox { Content = "Разрешить замену заполненных переводов", Margin = new Thickness(8) };
        var grid = Table(rows, ("Категория", "Kind"), ("Оценка", "Score"), ("Файл", "FilePath"), ("Key", "Key"), ("Старый ключ", "OldKey"), ("Старый source", "OldSource"), ("Новый source", "Source"), ("Было", "Before"), ("Предложение", "After"), ("Подтверждён", "Confirmed"));
        grid.IsReadOnly = false; foreach (var col in grid.Columns) col.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        overwrite.Checked += (_, _) => { rows.Clear(); grid.ItemsSource = null; }; overwrite.Unchecked += (_, _) => { rows.Clear(); grid.ItemsSource = null; };
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Предыдущие файлы…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "NDJSON/JSONL|*.ndjson;*.jsonl" };
            if (dialog.ShowDialog(this) != true) return;
            try { var old = new List<LocalizationDocument>(); foreach (var path in dialog.FileNames) old.Add(await new NdjsonLocalizationAdapter().LoadAsync(path)); rows = PatchMigrationService.Preview(await vm.WojdTargetsAsync(), old, overwrite.IsChecked == true); grid.ItemsSource = rows; Status.Text = $"Предложений: {rows.Count}; неподтверждённые похожие строки не применяются."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Подтвердить выбранное предложение", (_, _) =>
        {
            if (grid.SelectedItem is not MigrationRow row) return;
            if (AppDialog.Show($"Проверьте контекст и QA.\n\n{row.OldSource}\n→ {row.Source}\n\nПеревод: {row.After}", "Ручное подтверждение", MessageBoxButton.YesNo, owner: this) == MessageBoxResult.Yes)
            { row.Confirmed = true; row.Include = true; grid.Items.Refresh(); }
        }));
        bar.Children.Add(ActionButton("Применить отмеченные", (_, _) =>
        {
            try
            {
                if (vm.IsBusy) return; grid.CommitEdit(); grid.CommitEdit();
                var changes = PatchMigrationService.Changes(rows); vm.ApplyBatch(changes, "Миграция патча");
                var selected = rows.Where(r => r.Include && r.Confirmed).Select(r => new { r.Kind, r.FilePath, r.Namespace, r.Key, r.OldKey, r.Source, r.OldSource, r.Before, r.After, AtUtc = DateTime.UtcNow }).ToList();
                ProjectMetadataService.Save(Path.Combine(vm.ProjectDataDirectory, "migration-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json"), selected);
                foreach (var row in rows) row.Include = false; grid.Items.Refresh(); Status.Text = $"Перенесено: {changes.Count}; доступен Undo. Проверьте QA перед сохранением.";
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); AddToolbar(overwrite); Body.Children.Add(grid);
    }
}
