using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class CollisionWindow : WorkflowWindow
{
    public CollisionWindow(MainViewModel vm) : base("Коллизии Namespace + Key")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "collisions.json");
        var choices = ProjectMetadataService.Load<List<CollisionChoice>>(path, () => []);
        var rows = new List<CollisionCandidate>(); var grid = Table(rows, ("Namespace", "Namespace"), ("Key", "Key"), ("Файл", "FilePath"), ("Строка", "Index"), ("Оригинал", "Source"), ("Перевод", "Translation"), ("Решение", "Decision"));
        var bar = new WrapPanel();
        async Task Refresh() { rows = CollisionService.Analyze(await vm.WojdTargetsAsync(), choices); grid.ItemsSource = rows; Status.Text = $"Кандидатов: {rows.Count}. Выбор применяется к рабочим процессам; физические строки сохраняются."; }
        void Choose(string rule)
        {
            if (grid.SelectedItem is not CollisionCandidate row) return;
            choices.RemoveAll(c => c.Namespace == row.Namespace && c.Key == row.Key);
            choices.Add(new(row.Namespace, row.Key, row.FilePath, row.Index, CollisionService.SourceHash(row.Source), rule, DateTime.UtcNow));
            ProjectMetadataService.Save(path, choices);
        }
        bar.Children.Add(ActionButton("Обновить", async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Выбрать этого кандидата", async (_, _) => { try { Choose("Ручной выбор"); await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Заблокировать группу", async (_, _) => { try { Choose("Заблокировано"); await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Сбросить решение", async (_, _) => { try { if (grid.SelectedItem is CollisionCandidate row) { choices.RemoveAll(c => c.Namespace == row.Namespace && c.Key == row.Key); ProjectMetadataService.Save(path, choices); await Refresh(); } } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Экспорт нерешённых…", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "unresolved-collisions.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { if (File.Exists(dialog.FileName)) throw new IOException("Выберите новое имя."); File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(rows.GroupBy(r => (r.Namespace, r.Key)).Where(g => !g.Any(r => r.Decision.StartsWith("Выбран"))).SelectMany(g => g).Select(r => new { r.Namespace, r.Key, r.FilePath, r.Index, r.Source, r.Translation, r.Decision }), new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        grid.MouseDoubleClick += async (_, _) => { if (grid.SelectedItem is CollisionCandidate row) { await vm.LoadPathAsync(row.FilePath); vm.GoTo(row.Index.ToString()); } };
        AddToolbar(bar); Body.Children.Add(grid); Loaded += async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
