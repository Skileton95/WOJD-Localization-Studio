using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class CollaborationWindow : WorkflowWindow
{
    public CollaborationWindow(MainViewModel vm) : base("Совместная работа и назначения")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "collaboration.json");
        var state = ProjectMetadataService.Load<CollaborationState>(path, () => new()); CollaborationService.Validate(state);
        var rows = new ObservableCollection<WorkAssignment>(state.Assignments);
        var author = new TextBox { Text = state.Author, Width = 200, Margin = new Thickness(8) };
        var grid = Table(rows, ("Namespace (пусто: все)", "Namespace"), ("Исполнитель", "Assignee"), ("Проверяющий", "Reviewer"), ("Статус", "Status"), ("Заметка", "Note")); grid.IsReadOnly = false;
        var bar = new WrapPanel(); bar.Children.Add(new TextBlock { Text = "Автор", Margin = new Thickness(8) }); bar.Children.Add(author);
        bar.Children.Add(ActionButton("Добавить назначение", (_, _) => rows.Add(new() { Namespace = vm.SelectedEntry?.Namespace ?? "" })));
        bar.Children.Add(ActionButton("Удалить назначение", (_, _) => { if (grid.SelectedItem is WorkAssignment row) rows.Remove(row); }));
        bar.Children.Add(ActionButton("Сохранить", (_, _) =>
        {
            try { grid.CommitEdit(); grid.CommitEdit(); state.Author = author.Text; state.Assignments = rows.ToList(); CollaborationService.Validate(state); ProjectMetadataService.Save(path, state); Status.Text = "Назначения сохранены."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Экспорт задания…", async (_, _) =>
        {
            if (grid.SelectedItem is not WorkAssignment assignment) { Status.Text = "Выберите назначение."; return; }
            var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "WOJD-task.json" }; if (save.ShowDialog(this) != true) return;
            try
            {
                grid.CommitEdit(); grid.CommitEdit(); if (File.Exists(save.FileName)) throw new IOException("Выберите новое имя.");
                var docs = await vm.WojdTargetsAsync(); var root = vm.Project?.Root ?? (vm.ActiveDocument is null ? throw new IOException("Нет проекта.") : Path.GetDirectoryName(vm.ActiveDocument.FilePath)!);
                var bundle = CollaborationService.Export(root, vm.Project?.Name ?? "NDJSON", vm.Project?.GameVersion ?? "", assignment, author.Text, docs);
                File.WriteAllText(save.FileName, JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true })); Status.Text = $"Экспортировано строк: {bundle.Rows.Count}.";
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Импорт предложений…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var bundle = JsonSerializer.Deserialize<TaskBundle>(File.ReadAllText(dialog.FileName)) ?? throw new IOException("Пустое задание.");
                var docs = await vm.WojdTargetsAsync(); var root = vm.Project?.Root ?? (vm.ActiveDocument is null ? throw new IOException("Нет проекта.") : Path.GetDirectoryName(vm.ActiveDocument.FilePath)!);
                new TaskImportWindow(vm, CollaborationService.Preview(root, docs, bundle)) { Owner = this }.ShowDialog();
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(grid); Status.Text = "Статусы: Запланировано / В работе / На проверке / Завершено. Обмен заданиями — через файлы; source всегда проверяется.";
    }
}
public sealed class TaskImportWindow : WorkflowWindow
{
    public TaskImportWindow(MainViewModel vm, List<TaskImportRow> rows) : base("Просмотр предложений")
    {
        var grid = Table(rows, ("Результат", "Kind"), ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Key", "Key"), ("Source", "Source"), ("Сейчас", "Before"), ("Предложение", "Proposal"));
        grid.IsReadOnly = false; foreach (var column in grid.Columns) column.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Подтвердить конфликт базы…", (_, _) =>
        {
            if (grid.SelectedItem is not TaskImportRow row || row.Kind != "Конфликт базы") return;
            if (AppDialog.Show($"Текущий перевод:\n{row.Before}\n\nПредложение:\n{row.Proposal}\n\nПринять замену после проверки?", "Конфликт перевода", MessageBoxButton.YesNo, owner: this) == MessageBoxResult.Yes) { row.ManualConfirmed = true; row.Include = true; grid.Items.Refresh(); }
        }));
        bar.Children.Add(ActionButton("Применить отмеченные", (_, _) =>
        {
            try { if (vm.IsBusy) return; grid.CommitEdit(); grid.CommitEdit(); var changes = CollaborationService.Changes(rows); vm.ApplyBatch(changes, "Предложения коллег"); foreach (var row in rows) row.Include = false; grid.Items.Refresh(); Status.Text = $"Применено: {changes.Count}; доступен общий Undo."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(grid);
    }
}
