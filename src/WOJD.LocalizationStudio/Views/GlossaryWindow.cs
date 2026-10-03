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
public sealed class GlossaryWindow : WorkflowWindow
{
    public GlossaryWindow(MainViewModel vm) : base("Глоссарий проекта")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "glossary.json");
        var rows = new ObservableCollection<GlossaryTerm>(GlossaryService.Load(path));
        var grid = Table(rows, ("CN", "Chinese"), ("RU", "Russian"), ("Запрещены (через |)", "Forbidden"), ("Категория", "Category"), ("Namespace (пусто: все)", "Namespace"), ("Комментарий", "Comment"));
        grid.IsReadOnly = false;
        grid.Columns.Insert(2, new DataGridCheckBoxColumn { Header = "Обязательный", Binding = new Binding("Required") { Mode = BindingMode.TwoWay } });
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Добавить", (_, _) => rows.Add(new())));
        bar.Children.Add(ActionButton("Удалить", (_, _) => { if (grid.SelectedItem is GlossaryTerm term) rows.Remove(term); }));
        bar.Children.Add(ActionButton("Сохранить", (_, _) =>
        {
            try { grid.CommitEdit(); grid.CommitEdit(); GlossaryService.Save(path, rows.ToList()); vm.ReloadGlossary(); Status.Text = $"Терминов: {rows.Count}. Сохранено."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Импорт с просмотром…", (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Глоссарий JSON|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var incoming = GlossaryService.Load(dialog.FileName);
                new ReportWindow("Входящие термины", incoming, ("CN", "Chinese"), ("RU", "Russian"), ("Обязательный", "Required"), ("Запрет", "Forbidden"), ("Комментарий", "Comment")) { Owner = this }.ShowDialog();
                if (AppDialog.Show("Добавить/заменить термины с совпадающим Id в текущем списке? Изменения сохраняются отдельной кнопкой.", "Импорт глоссария", MessageBoxButton.YesNo, owner: this) != MessageBoxResult.Yes) return;
                foreach (var term in incoming) { var old = rows.FirstOrDefault(t => t.Id == term.Id); if (old is not null) rows.Remove(old); rows.Add(term); }
                Status.Text = "Импорт в список выполнен. Проверьте и сохраните.";
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Экспорт…", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "glossary-export.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { grid.CommitEdit(); grid.CommitEdit(); if (File.Exists(dialog.FileName)) throw new IOException("Выберите новый файл."); GlossaryService.Validate(rows.ToList()); File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(grid);
        Status.Text = "* Обязательный вариант проверяется QA. Namespace ограничивает правило; пустое поле применяет его ко всему проекту.";
    }
}
