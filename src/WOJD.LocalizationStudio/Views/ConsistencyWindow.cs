using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;

public sealed class ConsistencyWindow : WorkflowWindow
{
    public ConsistencyWindow(MainViewModel vm) : base("Дубликаты и единообразие перевода")
    {
        var report = ConsistencyService.Analyze(vm.OpenDocuments);
        Status.Text = "Анализ открытых файлов. Повторяющиеся китайские фрагменты — кандидаты для словаря; обратные совпадения требуют проверки контекста.";
        var groups = Table(report.Duplicates, ("Оригинал", "Text"), ("Строк", "Count"), ("Варианты перевода", "VariantText"));
        var variants = new ComboBox { MinWidth = 260, Margin = new Thickness(0,0,8,8) };
        var locations = Table(null!, ("Файл", "FilePath"), ("Namespace", "Entry.Namespace"), ("Ключ", "Entry.Key"), ("Перевод", "Entry.Translation"));
        groups.SelectionChanged += (_, _) =>
        {
            if (groups.SelectedItem is not ConsistencyGroup group) return;
            variants.ItemsSource = group.Variants; variants.SelectedIndex = 0;
            locations.ItemsSource = group.Locations;
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(variants);
        actions.Children.Add(ActionButton("Применить вариант ко всем одинаковым оригиналам", (_, _) =>
        {
            if (groups.SelectedItem is not ConsistencyGroup group || variants.SelectedItem is not string value) return;
            vm.ApplyBatch(group.Locations.Select(x => (x.Entry, WOJD.LocalizationStudio.Models.EntryField.Translation, x.Entry.Translation, value)));
            vm.RefreshConsistency();
            Status.Text = $"Применено к {group.Count} строкам. Изменения остаются несохранёнными; доступна отмена в сессиях файлов.";
        }));
        actions.Children.Add(ActionButton("Фильтр «Несогласованные»", (_, _) => { vm.StatusFilter = "Несогласованные"; Close(); }));
        actions.Children.Add(ActionButton("Разрешить / отменить разные переводы этого source", (_, _) =>
        {
            if (groups.SelectedItem is not ConsistencyGroup group) return;
            Status.Text = vm.ToggleConsistencyException(group.Text) ? "Разные переводы этого source разрешены во всём проекте." : "Исключение удалено.";
        }));
        locations.MouseDoubleClick += (_, _) =>
        {
            if (locations.SelectedItem is not EntryLocation row) return;
            vm.OpenProjectSearchResult(new ProjectSearchResult(row.FilePath, System.IO.Path.GetFileName(row.FilePath), row.Entry.Index, row.Entry.Namespace, row.Entry.Key, "", "", row.Entry));
            Close();
        };
        AddToolbar(actions);
        var tabs = new TabControl();
        var conflict = new Grid();
        conflict.RowDefinitions.Add(new RowDefinition()); conflict.RowDefinitions.Add(new RowDefinition());
        conflict.Children.Add(groups); Grid.SetRow(locations, 1); conflict.Children.Add(locations);
        tabs.Items.Add(new TabItem { Header = $"Повторы ({report.Duplicates.Count}) · конфликты ({report.Conflicts.Count})", Content = conflict });
        tabs.Items.Add(new TabItem { Header = "Один перевод у разных оригиналов", Content = Table(report.SharedTranslations, ("Перевод", "Text"), ("Строк", "Count"), ("Оригиналы", "VariantText")) });
        tabs.Items.Add(new TabItem { Header = "Повторяющиеся фрагменты", Content = Table(report.FrequentTerms, ("Фрагмент", "Text"), ("Использований", "Count"), ("Переводы", "VariantText")) });
        Body.Children.Add(tabs);
    }
}