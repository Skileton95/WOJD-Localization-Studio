using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ReleasePackageWindow : WorkflowWindow
{
    public ReleasePackageWindow(MainViewModel vm) : base("Пакет перевода")
    {
        var baselinePath = Path.Combine(vm.ProjectDataDirectory, "release-baseline.json");
        var previous = ProjectMetadataService.Load<ReleaseBaseline?>(baselinePath, () => null);
        var version = new TextBox { Text = vm.Project?.GameVersion ?? "", Width = 160, Margin = new Thickness(4) };
        var comment = new TextBox { Text = "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), Height = 80, ToolTip = "Комментарий к changelog" };
        var draft = new CheckBox { Content = "Разрешить предупреждения: черновой пакет", Margin = new Thickness(8) };
        var remember = new CheckBox { Content = "Сделать пакет новой базовой версией", IsChecked = true, Margin = new Thickness(8) };
        var grid = Table(new List<object>(), ("Категория", "Kind"), ("До", "Before"), ("После", "After"));
        WojdQaProfile Profile() => ProjectMetadataService.Load<WojdQaProfile>(Path.Combine(vm.ProjectDataDirectory, "qa-profile.json"), () => new());
        List<TermQaException> Exceptions() => ProjectMetadataService.Load<List<TermQaException>>(Path.Combine(vm.ProjectDataDirectory, "term-exceptions.json"), () => []);
        List<CollisionChoice> Choices() => ProjectMetadataService.Load<List<CollisionChoice>>(Path.Combine(vm.ProjectDataDirectory, "collisions.json"), () => []);
        var bar = new WrapPanel(); bar.Children.Add(new TextBlock { Text = "Версия перевода", Margin = new Thickness(8) }); bar.Children.Add(version);
        bar.Children.Add(ActionButton("Просмотр изменений / QA…", async (_, _) =>
        {
            try { var docs = await vm.WojdTargetsAsync(); grid.ItemsSource = ReleasePackageService.Changes(ReleasePackageService.Snapshot(version.Text, docs), previous); var check = ReleasePackageService.Preflight(docs, Profile(), vm.Glossary, Exceptions(), Choices()); new ReportWindow("QA перед упаковкой", check.Errors.Select(s => new { Level = "Ошибка", Detail = s }).Concat(check.Warnings.Select(s => new { Level = "Предупреждение", Detail = s })).ToList(), ("Уровень", "Level"), ("Сведения", "Detail")) { Owner = this }.ShowDialog(); Status.Text = $"База: {previous?.Version ?? "нет"}; ошибок: {check.Errors.Count}; предупреждений: {check.Warnings.Count}."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        bar.Children.Add(ActionButton("Создать ZIP…", async (_, _) =>
        {
            var save = new SaveFileDialog { Filter = "ZIP|*.zip", FileName = "WOJD-translation-" + version.Text.Replace('/', '_').Replace('\\', '_') + ".zip" };
            if (save.ShowDialog(this) != true) return;
            try
            {
                var docs = await vm.WojdTargetsAsync();
                var next = await ReleasePackageService.CreateAsync(save.FileName, version.Text, comment.Text, vm.Project?.Root, docs, previous, Profile(), vm.Glossary, Exceptions(), Choices(), draft.IsChecked == true);
                ProjectMetadataService.Save(Path.Combine(vm.ProjectDataDirectory, "releases", version.Text + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json"), next);
                if (remember.IsChecked == true) { ProjectMetadataService.Save(baselinePath, next); previous = next; }
                Status.Text = "Пакет создан: " + save.FileName + "\nSHA-256: " + FileSafetyService.Hash(save.FileName);
            } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(bar); AddToolbar(draft); AddToolbar(remember); AddToolbar(comment); Body.Children.Add(grid);
        Status.Text = "Экспорт включает текущие правки в памяти; оригиналы не сохраняются автоматически. Предыдущие пакеты не перезаписываются.";
    }
}
