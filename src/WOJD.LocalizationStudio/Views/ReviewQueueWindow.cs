using System.IO;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ReviewQueueWindow : WorkflowWindow
{
    public ReviewQueueWindow(MainViewModel vm) : base("Очередь проверки WOJD")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "reviews.json"); var baselinePath = Path.Combine(vm.ProjectDataDirectory, "review-baseline.json");
        var decisions = ProjectMetadataService.Load<List<ReviewDecision>>(path, () => []);
        var baseline = ProjectMetadataService.Load<List<ReviewBaselineRow>>(baselinePath, () => []);
        var rows = new List<ReviewQueueRow>(); var grid = Table(rows, ("Статус", "Status"), ("Причины", "Reasons"), ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Key", "Key"), ("Source", "Source"), ("Перевод", "Translation"), ("Комментарий", "Comment"));
        var filter = new ComboBox { ItemsSource = new[] { "Ожидает", "Отложено", "Проверено", "Все" }, SelectedIndex = 0, Width = 150 };
        void Filter() => grid.ItemsSource = filter.Text == "Все" ? rows : rows.Where(r => r.Status == filter.Text).ToList();
        filter.SelectionChanged += (_, _) => Filter();
        async Task Refresh()
        {
            var docs = await vm.WojdTargetsAsync();
            rows = ReviewQueueService.Build(docs, vm.Glossary,
                ProjectMetadataService.Load<List<TermQaException>>(Path.Combine(vm.ProjectDataDirectory, "term-exceptions.json"), () => []),
                ProjectMetadataService.Load<WojdQaProfile>(Path.Combine(vm.ProjectDataDirectory, "qa-profile.json"), () => new()), decisions, baseline, vm.IntentionalConsistency);
            Filter(); Status.Text = $"Всего: {rows.Count}; проверено: {rows.Count(r => r.Status == "Проверено")}; отложено: {rows.Count(r => r.Status == "Отложено")}; ожидает: {rows.Count(r => r.Status == "Ожидает")}.";
        }
        async Task Decide(string status)
        {
            if (grid.SelectedItem is not ReviewQueueRow row) return;
            var comment = "";
            if (status == "Отложено") { var input = new TextInputDialog("Отложить", "Причина/вопрос для следующей проверки.") { Owner = this }; if (input.ShowDialog() != true) return; comment = input.Value; }
            var decision = ReviewQueueService.Decide(row, status, comment, Environment.UserName);
            decisions.RemoveAll(d => d.FilePath == row.FilePath && d.Index == row.Index && d.Namespace == row.Namespace && d.Key == row.Key);
            decisions.Add(decision); ProjectMetadataService.Save(path, decisions); await Refresh();
        }
        var bar = new WrapPanel(); bar.Children.Add(filter);
        bar.Children.Add(ActionButton("Обновить", async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Принять", async (_, _) => { try { await Decide("Проверено"); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Отложить…", async (_, _) => { try { await Decide("Отложено"); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Вернуть в очередь", async (_, _) => { try { await Decide("Ожидает"); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Исправить…", async (_, _) =>
        {
            if (grid.SelectedItem is not ReviewQueueRow row) return;
            try { await vm.LoadPathAsync(row.FilePath); vm.GoTo(row.Index.ToString()); new ExpandedEditorWindow(vm) { Owner = this }.ShowDialog(); await Refresh(); }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Зафиксировать базовый source…", async (_, _) =>
        {
            if (AppDialog.Show("Запомнить текущие оригиналы как базовую версию для обнаружения новых/изменённых строк следующего патча?", "Базовая версия", MessageBoxButton.YesNo, owner: this) != MessageBoxResult.Yes) return;
            try { baseline = ReviewQueueService.Baseline(await vm.WojdTargetsAsync()); ProjectMetadataService.Save(baselinePath, baseline); await Refresh(); } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); Body.Children.Add(grid); Loaded += async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
