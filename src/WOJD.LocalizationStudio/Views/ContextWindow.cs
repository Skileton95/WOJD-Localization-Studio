using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ContextWindow : WorkflowWindow
{
    public ContextWindow(MainViewModel vm) : base("Контекст строки")
    {
        var rows = new List<ContextRow>(); var grid = Table(rows, ("Связь", "Kind"), ("Файл", "FilePath"), ("Строка", "Index"), ("Namespace", "Namespace"), ("Key", "Key"), ("Source", "Source"), ("Перевод", "Translation"));
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), MaxHeight = 100 };
        void Refresh()
        {
            if (vm.ActiveDocument is null || vm.SelectedEntry is null) { Status.Text = "Выберите строку."; return; }
            var entry = vm.SelectedEntry;
            rows = ContextService.Analyze(vm.OpenDocuments.ToList(), vm.ActiveDocument.FilePath, entry);
            rows.AddRange(ContextService.Historical(vm.ProjectDataDirectory, vm.ActiveDocument.FilePath, entry)); grid.ItemsSource = rows;
            var note = vm.GetAnnotation(entry); details.Text = vm.GlossaryHints + (note is null ? "" : "\nЗаметка: " + note.Status + " — " + note.Note);
            Status.Text = "Повторы ищутся в открытых файлах. Для полного проекта нажмите загрузку. Связи по ключу — подсказки; история показывает сохранённые снимки.";
        }
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Обновить", (_, _) => { try { Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Загрузить RU-проект", async (_, _) =>
        {
            try { var file = vm.ActiveDocument?.FilePath; var index = vm.SelectedEntry?.Index; await vm.WojdTargetsAsync(); if (file is not null) { await vm.LoadPathAsync(file); if (index.HasValue) vm.GoTo(index.Value.ToString()); } Refresh(); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("CN / EN проекта", async (_, _) =>
        {
            try
            {
                if (vm.Project is null || vm.SelectedEntry is null) return; var entry = vm.SelectedEntry;
                var source = await WojdProjectService.ReadAsync(vm.Project);
                rows.AddRange(source.Texts.Where(t => t.Namespace == entry.Namespace && t.Key == entry.Key && t.Role != "RU").Select(t => new ContextRow("Источник " + t.Role, t.FilePath, t.Index, t.Namespace, t.Key, t.Source, t.Text, null)));
                grid.Items.Refresh(); Status.Text = string.Join("\n", source.Issues);
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Предыдущая", (_, _) => { vm.PreviousCommand.Execute(null); Refresh(); }));
        bar.Children.Add(ActionButton("Следующая", (_, _) => { vm.NextCommand.Execute(null); Refresh(); }));
        grid.MouseDoubleClick += async (_, _) =>
        {
            if (grid.SelectedItem is not ContextRow { Location: not null } row) return;
            try { await vm.LoadPathAsync(row.FilePath); var entry = vm.ActiveDocument?.Entries.FirstOrDefault(e => e.Index == row.Index && e.Key == row.Key && e.Namespace == row.Namespace && e.Original == row.Source); if (entry is null) { Status.Text = "Контекст устарел. Обновите."; return; } vm.SelectedEntry = entry; Refresh(); }
            catch (Exception e) { Status.Text = e.Message; }
        };
        AddToolbar(bar); AddToolbar(details); Body.Children.Add(grid); Loaded += (_, _) => { try { Refresh(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
