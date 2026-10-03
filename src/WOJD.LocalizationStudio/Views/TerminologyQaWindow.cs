using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class TerminologyQaWindow : WorkflowWindow
{
    public TerminologyQaWindow(MainViewModel vm) : base("Терминология QA")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "term-exceptions.json");
        var exceptions = ProjectMetadataService.Load<List<TermQaException>>(path, () => []);
        var rows = new List<TermQaIssue>();
        var grid = Table(rows, ("Проблема", "Code"), ("Сведения", "Detail"), ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Ключ", "Key"), ("Source", "Source"), ("Перевод", "Translation"));
        async Task Refresh() { rows = TerminologyQaService.Analyze(await vm.WojdTargetsAsync(), vm.Glossary, exceptions); grid.ItemsSource = rows; Status.Text = $"Замечаний: {rows.Count}. Для склонений используйте явные формы RU через |."; }
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Проверить проект", async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Исключение…", async (_, _) =>
        {
            if (grid.SelectedItem is not TermQaIssue issue) return;
            var input = new TextInputDialog("Исключение QA", "Укажите причину для этой версии строки.") { Owner = this };
            if (input.ShowDialog() != true) return;
            try { exceptions.Add(TerminologyQaService.Except(issue, input.Value)); ProjectMetadataService.Save(path, exceptions); await Refresh(); } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Просмотр замены запрета…", async (_, _) =>
        {
            if (grid.SelectedItem is not TermQaIssue issue || issue.Matched.Length == 0 || issue.Term.Russian.Length == 0) { Status.Text = "Выберите запрещённый вариант с русским предложением."; return; }
            try
            {
                var affected = rows.Where(r => r.Term.Id == issue.Term.Id && r.Matched == issue.Matched).Select(r => r.Location.Entry).ToHashSet();
                var candidates = ProjectReplaceService.Preview(await vm.WojdTargetsAsync(), EntryField.Translation, issue.Matched, TerminologyQaService.Forms(issue.Term.Russian)[0], false, false, true).Where(c => affected.Contains(c.Entry)).ToList();
                new TermReplacementWindow(vm, candidates) { Owner = this }.ShowDialog(); await Refresh();
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Список исключений…", async (_, _) => { new TermExceptionsWindow(path, exceptions) { Owner = this }.ShowDialog(); await Refresh(); }));
        bar.Children.Add(ActionButton("Удалить исключения этой строки", async (_, _) =>
        {
            if (grid.SelectedItem is not TermQaIssue issue) return;
            exceptions.RemoveAll(e => e.FilePath == issue.FilePath && e.Namespace == issue.Namespace && e.Key == issue.Key);
            ProjectMetadataService.Save(path, exceptions); await Refresh();
        }));
        grid.MouseDoubleClick += async (_, _) => { if (grid.SelectedItem is TermQaIssue issue) { await vm.LoadPathAsync(issue.FilePath); vm.GoTo(issue.Index.ToString()); } };
        AddToolbar(bar); Body.Children.Add(grid); Loaded += async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
public sealed class TermReplacementWindow : WorkflowWindow
{
    public TermReplacementWindow(MainViewModel vm, List<ReplaceCandidate> rows) : base("Предварительный просмотр замены термина")
    {
        var grid = Table(rows, ("Файл", "FilePath"), ("Было", "Before"), ("Будет", "After")); grid.IsReadOnly = false; foreach (var col in grid.Columns) col.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        AddToolbar(ActionButton("Применить отмеченные", (_, _) =>
        {
            try { if (vm.IsBusy) return; grid.CommitEdit(); grid.CommitEdit(); vm.ApplyBatch(rows.Where(r => r.Include).Select(r => (r.Entry, r.Field, r.Before, r.After)), "Замена термина"); DialogResult = true; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        Body.Children.Add(grid); Status.Text = $"Строк: {rows.Count}. Можно исключить отдельные строки; вся замена отменяется одним Undo.";
    }
}

public sealed class TermExceptionsWindow : WorkflowWindow
{
    public TermExceptionsWindow(string path, List<TermQaException> rows) : base("Исключения терминологии")
    {
        var grid = Table(rows, ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Key", "Key"), ("Проверка", "Code"), ("Причина", "Reason"));
        AddToolbar(ActionButton("Удалить выбранное исключение", (_, _) =>
        { try { if (grid.SelectedItem is TermQaException row) { rows.Remove(row); ProjectMetadataService.Save(path, rows); grid.Items.Refresh(); } } catch (Exception e) { Status.Text = e.Message; } }));
        Body.Children.Add(grid);
    }
}
