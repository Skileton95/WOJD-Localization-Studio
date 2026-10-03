using System.IO;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class SqliteIndexWindow : WorkflowWindow
{
    public SqliteIndexWindow(MainViewModel vm) : base("SQLite-индекс проекта")
    {
        var grid = Table(new List<IndexFileSummary>(), ("Файл", "FilePath"), ("Строк", "Rows"), ("SHA-256", "Sha256")); var bar = new WrapPanel();
        void ShowStats() { grid.ItemsSource = SqliteProjectIndexService.Statistics(); Status.Text = "Индекс: " + SqliteProjectIndexService.DatabasePath + "\nОткрытые правки используются напрямую из памяти; БД содержит версии с диска."; }
        async Task Build()
        {
            var files = vm.ProjectFilePaths; var progress = new Progress<FileOperationProgress>(p => Status.Text = p.Stage);
            bar.IsEnabled = false;
            try { foreach (var path in files) await SqliteProjectIndexService.EnsureAsync(path, default, progress);
                var metadata = new[] { "glossary.json", "collisions.json", "reviews.json", "qa-profile.json", "term-exceptions.json", "collaboration.json", "review-baseline.json", "release-baseline.json", "edit-history.jsonl" }.Select(n => Path.Combine(vm.ProjectDataDirectory, n))
                    .Append(Path.Combine(WorkspaceStateService.StorageDirectory, "annotations.json"));
                await SqliteProjectIndexService.ProjectMetadataAsync(metadata); ShowStats();
            } catch (Exception e) { Status.Text = e.Message; } finally { bar.IsEnabled = true; }
        }
        bar.Children.Add(ActionButton("Обновить индекс и metadata", async (_, _) => await Build()));
        bar.Children.Add(ActionButton("Пересоздать…", async (_, _) =>
        {
            if (AppDialog.Show("Сохранить старую БД отдельно и пересоздать индекс из NDJSON/metadata?", "Индекс", MessageBoxButton.YesNo, owner: this) != MessageBoxResult.Yes) return;
            try { await SqliteProjectIndexService.ResetAsync(); await Build(); } catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Статистика", (_, _) => { try { ShowStats(); } catch (Exception e) { Status.Text = e.Message; } }));
        AddToolbar(bar); Body.Children.Add(grid); Loaded += (_, _) => { try { ShowStats(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
