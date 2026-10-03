using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class NotesWindow : WorkflowWindow
{
    public NotesWindow(MainViewModel vm) : base("Заметки и вопросы")
    {
        if (vm.SelectedEntry is not { } row) { Status.Text = "Выберите строку."; return; }
        var annotation = vm.GetAnnotation(row);
        var state = new ComboBox { ItemsSource = new[] { "", "Нужно проверить", "Термин", "Контекст неизвестен" }, SelectedItem = annotation?.Status ?? "", Margin = new Thickness(0,8,0,8) };
        var text = new TextBox { Text = annotation?.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
        AddToolbar(new TextBlock { Text = $"{row.Namespace} / {row.Key}\n{row.OriginalDisplay}", TextWrapping = TextWrapping.Wrap });
        AddToolbar(state);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Сохранить заметку", (_, _) => { try { vm.SetAnnotation(row, text.Text, (string)state.SelectedItem); Status.Text = "Заметка сохранена отдельно от NDJSON."; } catch (Exception e) { Status.Text = e.Message; } }));
        actions.Children.Add(ActionButton("Только строки с заметками", (_, _) => { vm.NotesOnly = true; Close(); }));
        actions.Children.Add(ActionButton("Экспорт вопросов…", async (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "translation-questions.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                await vm.EnsureProjectLoadedAsync();
                var questions = vm.OpenDocuments.SelectMany(d => d.Entries.Select(e => new { d.FilePath, e.Namespace, e.Key, e.Original, Annotation = vm.GetAnnotation(e) })).Where(x => x.Annotation is not null).ToList();
                await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(questions, new JsonSerializerOptions { WriteIndented = true }));
                Status.Text = $"Экспортировано: {questions.Count}.";
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(actions); Body.Children.Add(text);
        Status.Text = "Заметки привязаны к файлу, Namespace, ключу и source. В игровой NDJSON они не записываются.";
    }
}