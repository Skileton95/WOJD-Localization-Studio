using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class GitWindow : WorkflowWindow
{
    public GitWindow(MainViewModel vm) : base("Git проекта")
    {
        string? root = null; var rows = new List<GitChange>();
        var grid = Table(rows, ("Статус", "Status"), ("Путь", "Path"), ("Предыдущий путь", "OriginalPath"));
        grid.IsReadOnly = false; foreach (var col in grid.Columns) col.IsReadOnly = true;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn { Header = "Выбрать", Binding = new Binding("Include") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        var message = new TextBox { Width = 270, ToolTip = "Сообщение коммита", Margin = new Thickness(8) };
        var branch = new ComboBox { IsEditable = true, Width = 180, Margin = new Thickness(8) };
        var bar = new WrapPanel();
        bool Dirty() => vm.OpenDocuments.Any(d => d.Entries.Any(e => e.Status == TranslationStatus.Modified));
        async Task Refresh()
        {
            var directory = vm.Project?.Root ?? (vm.ActiveDocument is null ? null : Path.GetDirectoryName(vm.ActiveDocument.FilePath));
            if (directory is null) throw new IOException("Откройте проект/файл в рабочей копии Git.");
            root = await GitService.RootAsync(directory); rows = await GitService.StatusAsync(root); grid.ItemsSource = rows;
            branch.ItemsSource = (await GitService.RunAsync(root, "branch", "--format=%(refname:short)")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Status.Text = $"Репозиторий: {root}; изменений: {rows.Count}. Git работает с сохранёнными файлами.";
        }
        async Task Execute(Func<Task> work)
        {
            bar.IsEnabled = false;
            try { await work(); } catch (Exception e) { Status.Text = e.Message; }
            finally { bar.IsEnabled = true; }
        }
        bar.Children.Add(ActionButton("Обновить", async (_, _) => await Execute(Refresh)));
        bar.Children.Add(ActionButton("Diff файла", async (_, _) => await Execute(async () =>
        {
            if (root is null || grid.SelectedItem is not GitChange item) return;
            output.Text = item.Status == "??" ? File.ReadAllText(Path.Combine(root, item.Path)) : await GitService.RunAsync(root, "diff", "HEAD", "--", item.Path);
        })));
        bar.Children.Add(ActionButton("История", async (_, _) => await Execute(async () => { if (root is not null) output.Text = await GitService.RunAsync(root, "log", "-30", "--date=iso", "--format=%h %ad %an %s"); })));
        bar.Children.Add(ActionButton("История строки", async (_, _) => await Execute(async () =>
        {
            if (root is null || vm.ActiveDocument is null || vm.SelectedEntry is null) return;
            var line = vm.SelectedEntry.LineNumber; if (line <= 0) throw new IOException("Неизвестен номер физической строки.");
            output.Text = await GitService.RunAsync(root, "blame", "--line-porcelain", "-L", $"{line},{line}", "--", GitService.Relative(root, vm.ActiveDocument.FilePath));
        })));
        bar.Children.Add(message);
        bar.Children.Add(ActionButton("Коммит выбранных", async (_, _) => await Execute(async () =>
        {
            if (root is null) return; if (Dirty()) throw new IOException("Сначала сохраните правки редактора и проверьте diff.");
            grid.CommitEdit(); grid.CommitEdit(); var selected = rows.Where(r => r.Include).ToList();
            if (AppDialog.Show($"Создать локальный коммит выбранных файлов: {selected.Count}?\n{message.Text}", "Git commit", MessageBoxButton.YesNo, owner: this) != MessageBoxResult.Yes) return;
            output.Text = await GitService.CommitAsync(root, selected, message.Text); await Refresh();
        })));
        bar.Children.Add(branch);
        bar.Children.Add(ActionButton("Переключить ветку", async (_, _) => await Execute(async () => { if (root is null) return; if (Dirty()) throw new IOException("Сначала сохраните правки редактора."); await GitService.SwitchAsync(root, branch.Text, false); await Refresh(); })));
        bar.Children.Add(ActionButton("Создать ветку", async (_, _) => await Execute(async () => { if (root is null) return; if (Dirty()) throw new IOException("Сначала сохраните правки редактора."); await GitService.SwitchAsync(root, branch.Text, true); await Refresh(); })));
        AddToolbar(bar);
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition()); body.Children.Add(grid); Grid.SetRow(output, 1); body.Children.Add(output); Body.Children.Add(body);
        Loaded += async (_, _) => await Execute(Refresh);
        Status.Text = "Требуется установленный Git и существующий репозиторий. Автоматического pull/push нет; источники не объединяются без просмотра.";
    }
}
