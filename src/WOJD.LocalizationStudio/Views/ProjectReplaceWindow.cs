using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;

public sealed class ProjectReplaceWindow : WorkflowWindow
{
    public ProjectReplaceWindow(MainViewModel vm) : base("Замена по проекту — предварительный просмотр")
    {
        var query = new TextBox { Width = 230, Margin = new Thickness(0,0,8,8), ToolTip = "Найти" };
        var replacement = new TextBox { Width = 230, Margin = new Thickness(0,0,8,8), ToolTip = "Заменить на" };
        var field = new ComboBox { ItemsSource = new[] { "Перевод", "Оригинал", "Namespace", "Ключ" }, SelectedIndex = 0, Width = 130 };
        var regex = new CheckBox { Content = "Regex", Margin = new Thickness(8) };
        var matchCase = new CheckBox { Content = "Регистр", Margin = new Thickness(8) };
        var whole = new CheckBox { Content = "Целое слово", Margin = new Thickness(8) };
        var inputs = new WrapPanel(); foreach (var control in new UIElement[] { query, replacement, field, regex, matchCase, whole }) inputs.Children.Add(control);
        AddToolbar(inputs);
        var grid = Table(Array.Empty<ReplaceCandidate>(), ("Файл", "FilePath"), ("Namespace", "Entry.Namespace"), ("Ключ", "Entry.Key"), ("До", "Before"), ("После", "After"));
        grid.IsReadOnly = false;
        foreach (var col in grid.Columns) col.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Включить", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        var candidates = new List<ReplaceCandidate>();
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Предварительный просмотр", async (_, _) =>
        {
            try
            {
                await vm.EnsureProjectLoadedAsync();
                candidates = ProjectReplaceService.Preview(vm.OpenDocuments, (EntryField)field.SelectedIndex, query.Text, replacement.Text,
                    regex.IsChecked == true, matchCase.IsChecked == true, whole.IsChecked == true);
                grid.ItemsSource = candidates; Status.Text = $"Будет изменено строк: {candidates.Count}. Снимите галочки для исключения строк.";
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        actions.Children.Add(ActionButton("Применить отмеченные", (_, _) =>
        {
            try
            {
                grid.CommitEdit(); grid.CommitEdit();
                var chosen = candidates.Where(x => x.Include).ToList();
                vm.ApplyBatch(chosen.Select(x => (x.Entry, x.Field, x.Before, x.After)));
                Status.Text = $"Изменено строк: {chosen.Count}. Ctrl+Z в участвующем файле отменит всю операцию. Сохранение выполняется отдельно.";
                candidates.Clear(); grid.ItemsSource = null;
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(actions); Body.Children.Add(grid);
        Status.Text = "Поиск охватывает файлы дерева проекта; замена не сохраняет файлы автоматически. Regex использует синтаксис .NET.";
    }
}