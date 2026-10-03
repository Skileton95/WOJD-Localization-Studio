using System.IO;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class WojdQaWindow : WorkflowWindow
{
    public WojdQaWindow(MainViewModel vm) : base("WOJD QA")
    {
        var path = Path.Combine(vm.ProjectDataDirectory, "qa-profile.json");
        var profile = ProjectMetadataService.Load<WojdQaProfile>(path, () => new());
        var rows = new List<WojdQaIssue>(); var grid = Table(rows, ("Уровень", "Severity"), ("Проверка", "Code"), ("Сведения", "Detail"), ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Key", "Key"), ("Source", "Source"), ("Перевод", "Translation"));
        var level = new ComboBox { ItemsSource = new[] { "Все", "Ошибка", "Предупреждение" }, SelectedIndex = 0, Width = 160 };
        void Filter() => grid.ItemsSource = level.SelectedIndex == 0 ? rows : rows.Where(r => r.Severity == level.Text).ToList();
        level.SelectionChanged += (_, _) => Filter();
        async Task Refresh() { rows = WojdQaService.Analyze(await vm.WojdTargetsAsync(), profile); Filter(); Status.Text = $"Ошибок: {rows.Count(r => r.Severity == "Ошибка")}; предупреждений: {rows.Count(r => r.Severity != "Ошибка")}."; }
        var bar = new WrapPanel(); bar.Children.Add(level);
        bar.Children.Add(ActionButton("Проверить", async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        bar.Children.Add(ActionButton("Профиль…", async (_, _) => { new QaProfileWindow(path, profile) { Owner = this }.ShowDialog(); profile = ProjectMetadataService.Load<WojdQaProfile>(path, () => new()); try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } }));
        grid.MouseDoubleClick += async (_, _) => { if (grid.SelectedItem is WojdQaIssue issue) { await vm.LoadPathAsync(issue.FilePath); vm.GoTo(issue.Index.ToString()); } };
        AddToolbar(bar); Body.Children.Add(grid); Loaded += async (_, _) => { try { await Refresh(); } catch (Exception e) { Status.Text = e.Message; } };
    }
}
public sealed class QaProfileWindow : WorkflowWindow
{
    public QaProfileWindow(string path, WojdQaProfile profile) : base("Профиль QA")
    {
        var bar = new WrapPanel();
        foreach (var pair in new[] { ("Китайский остаток", nameof(profile.ChineseLeft)), ("Пустой перевод", nameof(profile.EmptyTranslation)), ("Длина", nameof(profile.SuspiciousLength)), ("Вложенность тегов", nameof(profile.TagBalance)), ("Управляющие символы", nameof(profile.Controls)), ("Равен source", nameof(profile.UnchangedTarget)) })
        {
            var property = typeof(WojdQaProfile).GetProperty(pair.Item2)!;
            var box = new CheckBox { Content = pair.Item1, IsChecked = (bool)property.GetValue(profile)!, Margin = new Thickness(8) };
            box.Checked += (_, _) => property.SetValue(profile, true); box.Unchecked += (_, _) => property.SetValue(profile, false); bar.Children.Add(box);
        }
        var minimum = new TextBox { Text = profile.MinRatio.ToString(), Width = 70 }; var maximum = new TextBox { Text = profile.MaxRatio.ToString(), Width = 70 };
        bar.Children.Add(new TextBlock { Text = "Минимум / максимум отношения длин", Margin = new Thickness(8) }); bar.Children.Add(minimum); bar.Children.Add(maximum); AddToolbar(bar);
        var rows = new System.Collections.ObjectModel.ObservableCollection<MarkerRule>(profile.Markers);
        var grid = Table(rows, ("Название", "Name"), ("Regex маркера", "Pattern")); grid.IsReadOnly = false;
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Добавить маркер", (_, _) => rows.Add(new())));
        actions.Children.Add(ActionButton("Удалить маркер", (_, _) => { if (grid.SelectedItem is MarkerRule row) rows.Remove(row); }));
        actions.Children.Add(ActionButton("Сохранить", (_, _) =>
        {
            try { grid.CommitEdit(); grid.CommitEdit(); profile.MinRatio = double.Parse(minimum.Text); profile.MaxRatio = double.Parse(maximum.Text); profile.Markers = rows.ToList(); WojdQaService.Validate(profile); ProjectMetadataService.Save(path, profile); DialogResult = true; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(actions); Body.Children.Add(grid); Status.Text = "Добавляйте только подтверждённые маркеры. QA сравнивает найденные токены; он не компилирует синтаксис движка и не заменяет тест в игре.";
    }
}
