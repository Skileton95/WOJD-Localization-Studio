using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class BinaryWorkflowWindow : WorkflowWindow
{
    public BinaryWorkflowWindow(MainViewModel vm) : base("locres / fmtstring — внешний конвертер")
    {
        string? original = null; BinaryConversionTicket? ticket = null;
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tool = new TextBox { Width = 220, Margin = new Thickness(4), ToolTip = "Название проверенного конвертера" };
        var version = new TextBox { Width = 110, Margin = new Thickness(4), ToolTip = "Версия конвертера" };
        var bar = new WrapPanel();
        bar.Children.Add(ActionButton("Выбрать оригинал…", (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Бинарные источники|*.locres;*.fmtstring|Все файлы|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try { original = dialog.FileName; ticket = null; details.Text = $"{original}\nБайт: {new FileInfo(original).Length}\nSHA-256: {FileSafetyService.Hash(original)}\n\nНативный бинарный провайдер отсутствует. Используйте проверенный внешний конвертер; выберите его NDJSON-экспорт ниже."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Сохранить копию…", (_, _) =>
        {
            if (original is null) return;
            var dialog = new SaveFileDialog { FileName = Path.GetFileName(original) + ".original-copy", Filter = "Копия|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try { BinaryWorkflowService.PreserveCopy(original, dialog.FileName); Status.Text = "Оригинальная копия проверена по SHA-256."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(new TextBlock { Text = "Инструмент / версия", Margin = new Thickness(8) }); bar.Children.Add(tool); bar.Children.Add(version);
        bar.Children.Add(ActionButton("Проверить NDJSON-экспорт…", async (_, _) =>
        {
            if (original is null) return;
            var dialog = new OpenFileDialog { Filter = "NDJSON/JSONL|*.ndjson;*.jsonl" };
            if (dialog.ShowDialog(this) != true) return;
            try { ticket = await BinaryWorkflowService.RegisterExportAsync(original, dialog.FileName, tool.Text, version.Text); details.Text += $"\n\nЭкспорт: {ticket.ExportPath}\nСтрок: {ticket.Rows}\nSHA-256: {ticket.ExportSha256}"; Status.Text = "Структура экспорта проверена. Корректность конвертации должен подтверждать внешний инструмент."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Открыть экспорт", async (_, _) => { if (ticket is not null) await vm.LoadPathAsync(ticket.ExportPath); }));
        bar.Children.Add(ActionButton("Отчёт происхождения…", (_, _) =>
        {
            if (ticket is null) return;
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "conversion-report.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { BinaryWorkflowService.SaveTicket(ticket, dialog.FileName); Status.Text = "Отчёт сохранён."; } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(details);
        Status.Text = "Редактируется только текстовый экспорт. Обратная сборка locres/fmtstring и проверка в игре выполняются внешним проверенным инструментом.";
    }
}
