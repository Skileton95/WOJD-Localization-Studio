using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class DiagnosticsWindow : WorkflowWindow
{
    public DiagnosticsWindow(MainViewModel vm) : base("Диагностический отчёт")
    {
        object report = DiagnosticsService.Collect(vm);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = System.Windows.TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) };
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Обновить", (_, _) => { report = DiagnosticsService.Collect(vm); text.Text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); }));
        bar.Children.Add(ActionButton("Экспорт…", (_, _) =>
        {
            var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "WOJD-diagnostics.json" }; if (save.ShowDialog(this) != true) return;
            try { DiagnosticsService.ExportNew(save.FileName, report); Status.Text = "Отчёт сохранён."; } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Самопроверка на синтетических данных", async (_, _) =>
        {
            bar.IsEnabled = false;
            try { await DiagnosticsService.SelfCheckAsync(); Status.Text = "Самопроверка прошла: NDJSON/QA/SQLite. Отчёт: " + System.IO.Path.Combine(WorkspaceStateService.StorageDirectory, "self-check.json"); }
            catch (Exception e) { Status.Text = e.Message; } finally { bar.IsEnabled = true; }
        }));
        AddToolbar(bar); Body.Children.Add(text); Status.Text = "Журналы: problems.log, safety.jsonl, startup-error.log в папке workspace. Игровые файлы самопроверка не изменяет.";
    }
}
