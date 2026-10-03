using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class SmartFiltersWindow : WorkflowWindow
{
    public SmartFiltersWindow(MainViewModel vm) : base("Умные фильтры")
    {
        Width = 740; Height = 540;
        var criteria = vm.SmartFilters;
        var labels = new[] { "Без перевода", "Ошибки QA", "Пустой оригинал", "Китайские символы в оригинале или переводе", "Латиница в оригинале или переводе", "Есть плейсхолдеры", "Длинный перевод (>100 и >2,5× оригинала)" };
        var values = new[] { criteria.Untranslated, criteria.Qa, criteria.EmptySource, criteria.Chinese, criteria.Latin, criteria.Placeholders, criteria.LongTranslation };
        var checks = labels.Select((label, i) => new CheckBox { Content = label, IsChecked = values[i], Margin = new Thickness(0,8,0,0) }).ToArray();
        var panel = new StackPanel(); foreach (var check in checks) panel.Children.Add(check);
        SmartFilter Read() => new(checks[0].IsChecked == true, checks[1].IsChecked == true, checks[2].IsChecked == true, checks[3].IsChecked == true, checks[4].IsChecked == true, checks[5].IsChecked == true, checks[6].IsChecked == true);
        var name = new TextBox { Margin = new Thickness(0,12,0,8), ToolTip = "Имя сохранённого фильтра" }; panel.Children.Add(name);
        var saved = new ComboBox { ItemsSource = vm.SavedFilters.ToList(), DisplayMemberPath = "Name", Margin = new Thickness(0,0,0,8) }; panel.Children.Add(saved);
        saved.SelectionChanged += (_, _) =>
        {
            if (saved.SelectedItem is not SavedFilter item) return;
            var f = item.Criteria; var flags = new[] { f.Untranslated, f.Qa, f.EmptySource, f.Chinese, f.Latin, f.Placeholders, f.LongTranslation };
            for (var i = 0; i < checks.Length; i++) checks[i].IsChecked = flags[i]; name.Text = item.Name;
        };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Применить", (_, _) => { vm.SetSmartFilters(Read()); Close(); }));
        actions.Children.Add(ActionButton("Сохранить набор", (_, _) => { try { vm.SaveFilter(name.Text.Trim(), Read()); saved.ItemsSource = vm.SavedFilters.ToList(); Status.Text = "Набор сохранён."; } catch (Exception e) { Status.Text = e.Message; } }));
        actions.Children.Add(ActionButton("Удалить набор", (_, _) => { if (saved.SelectedItem is SavedFilter item) { vm.SavedFilters.Remove(item); vm.SetSmartFilters(vm.SmartFilters); saved.ItemsSource = vm.SavedFilters.ToList(); } }));
        actions.Children.Add(ActionButton("Сбросить всё", (_, _) => { vm.ResetAllFilters(); Close(); }));
        AddToolbar(actions); Body.Children.Add(panel); Status.Text = "Все отмеченные условия применяются одновременно с поиском, статусом и Namespace.";
    }
}