using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ProjectStatisticsWindow : WorkflowWindow
{
    public ProjectStatisticsWindow(MainViewModel vm) : base("Статистика проекта")
    {
        ProjectStatistics? report = null;
        var columns = new[] { ("Группа", "Label"), ("Всего", "Total"), ("Переведено", "Translated"), ("Без перевода", "Untranslated"), ("Изменено", "Modified"), ("QA", "Qa"), ("Прогресс, %", "Progress") };
        var files = Table(null!, columns); var namespaces = Table(null!, columns); var problems = Table(null!, columns);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Файлы", Content = files }); tabs.Items.Add(new TabItem { Header = "Namespace", Content = namespaces });
        tabs.Items.Add(new TabItem { Header = "Проблемные Namespace", Content = problems });
        async Task Refresh()
        {
            try
            {
                Status.Text = "Подсчёт статистики…"; await vm.EnsureProjectLoadedAsync();
                report = await Task.Run(() => ProjectStatisticsService.Calculate(vm.OpenDocuments));
                files.ItemsSource = report.ByFile; namespaces.ItemsSource = report.ByNamespace;
                problems.ItemsSource = report.ByNamespace.Where(x => x.Qa > 0 || x.Untranslated > 0).OrderByDescending(x => x.Qa).ThenByDescending(x => x.Untranslated).ToList();
                Status.Text = $"Файлов: {report.Files} · строк: {report.Rows:N0} · переведено: {report.Translated:N0} · без перевода: {report.Untranslated:N0} · изменено: {report.Modified:N0} · QA: {report.Qa:N0}\nУникальных source: {report.UniqueSources:N0} · уникальных китайских: {report.UniqueChineseSources:N0} · повторных использований: {report.DuplicateOccurrences:N0}";
            }
            catch (Exception e) { Status.Text = e.Message; }
        }
        var actions = new WrapPanel(); actions.Children.Add(ActionButton("Обновить", async (_, _) => await Refresh()));
        actions.Children.Add(ActionButton("Экспорт CSV / JSON…", async (_, _) =>
        {
            if (report is null) return;
            var dialog = new SaveFileDialog { Filter = "JSON|*.json|CSV|*.csv", FileName = "project-statistics", DefaultExt = ".json" };
            if (dialog.ShowDialog(this) != true) return;
            try { await File.WriteAllTextAsync(dialog.FileName, dialog.FilterIndex == 2 ? ProjectStatisticsService.ToCsv(report) : JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(dialog.FilterIndex == 2)); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(actions); Body.Children.Add(tabs); Loaded += async (_, _) => await Refresh();
    }
}