using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class WojdProjectWindow : WorkflowWindow
{
    private WojdProject? _project;
    private readonly ObservableCollection<ProjectSource> _sources = [];
    public WojdProjectWindow(MainViewModel vm) : base("Проект WOJD")
    {
        _project = vm.Project;
        var name = new TextBox { Text = _project?.Name ?? "WOJD", Width = 180, Margin = new Thickness(4) };
        var version = new TextBox { Text = _project?.GameVersion ?? "", Width = 120, Margin = new Thickness(4) };
        var role = new ComboBox { ItemsSource = new[] { "CN", "EN", "RU" }, SelectedIndex = 2, Width = 70 };
        var format = new ComboBox { ItemsSource = new[] { "NDJSON", "fmtstring NDJSON", "locres" }, SelectedIndex = 0, Width = 160 };
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Новый проект…", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Проект WOJD|*.wojd-project.json", FileName = "WOJD.wojd-project.json" };
            if (dialog.ShowDialog(this) == true) { if (File.Exists(dialog.FileName)) { Status.Text = "Выберите новое имя или откройте существующий проект."; return; } _project = new WojdProject { ManifestPath = dialog.FileName }; _sources.Clear(); }
        }));
        bar.Children.Add(ActionButton("Открыть…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Проект WOJD|*.wojd-project.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { await vm.OpenWojdProjectAsync(dialog.FileName); _project = vm.Project; name.Text = _project!.Name; version.Text = _project.GameVersion; _sources.Clear(); foreach (var source in _project.Sources) _sources.Add(source); Status.Text = _project.Root; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(name); bar.Children.Add(version); AddToolbar(bar);
        var grid = Table(_sources, ("Путь", "Path"), ("Роль", "Role"), ("Происхождение", "Format"));
        var files = new WrapPanel(); files.Children.Add(role); files.Children.Add(format);
        files.Children.Add(ActionButton("Добавить файл…", (_, _) =>
        {
            if (_project is null) { Status.Text = "Создайте или откройте проект."; return; }
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "Источники|*.ndjson;*.jsonl;*.locres;*.fmtstring|Все файлы|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try { foreach (var path in dialog.FileNames) { var relative = Path.GetRelativePath(_project.Root, path); WojdProjectService.Resolve(_project, relative); if (!_sources.Any(s => s.Path.Equals(relative, StringComparison.OrdinalIgnoreCase))) _sources.Add(new() { Path = relative, Role = role.Text, Format = format.Text }); } }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        files.Children.Add(ActionButton("Убрать из проекта", (_, _) => { if (grid.SelectedItem is ProjectSource source) _sources.Remove(source); }));
        files.Children.Add(ActionButton("Сохранить и открыть RU", async (_, _) =>
        {
            try { if (_project is null) throw new IOException("Нет проекта."); _project.Name = name.Text; _project.GameVersion = version.Text; _project.Sources = _sources.ToList(); WojdProjectService.Save(_project); await vm.OpenWojdProjectAsync(_project.ManifestPath); Status.Text = "Проект сохранён: " + _project.Root; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        files.Children.Add(ActionButton("Общая таблица…", async (_, _) =>
        {
            try { if (_project is null) return; _project.Sources = _sources.ToList(); var result = await WojdProjectService.ReadAsync(_project); new ReportWindow("CN / EN / RU — источники на диске", result.Rows, ("Namespace", "Namespace"), ("Key", "Key"), ("CN", "Chinese"), ("EN", "English"), ("RU", "Russian"), ("Коллизия", "Collision"), ("Файлы", "Files")) { Owner = this }.ShowDialog(); Status.Text = string.Join("\n", result.Issues); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(files); Body.Children.Add(grid);
        foreach (var source in _project?.Sources ?? []) _sources.Add(source);
        Status.Text = "Версия игры; роли задаются явно. fmtstring NDJSON означает проверенный текстовый экспорт. Исходный бинарный fmtstring/locres не записывается.";
    }
}
