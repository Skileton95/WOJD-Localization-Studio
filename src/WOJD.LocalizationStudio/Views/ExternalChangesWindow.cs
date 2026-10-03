using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ExternalChangesWindow : WorkflowWindow
{
    public ExternalChangesWindow(MainViewModel vm, List<string> files) : base("Внешние изменения")
    {
        var selector = new ComboBox { ItemsSource = files, Margin = new Thickness(0,0,0,8) };
        var grid = Table(null!, ("Тип", "ChangeType"), ("Namespace", "Namespace"), ("Ключ", "Key"), ("На диске", "OldTranslation"), ("В редакторе", "NewTranslation"), ("Source на диске", "OldOriginal"), ("Source в редакторе", "NewOriginal"));
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem is not string path) return;
            try { await vm.LoadPathAsync(path); var disk = await new NdjsonLocalizationAdapter().LoadAsync(path); grid.ItemsSource = FileComparisonService.Compare(vm.ActiveDocument!, disk).Items; }
            catch (Exception e) { Status.Text = e.Message; }
        };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Перезагрузить", async (_, _) => { try { await vm.ReloadProtectedAsync(); Close(); } catch (Exception e) { Status.Text = e.Message; } }));
        actions.Children.Add(ActionButton("Оставить мою версию и сохранить…", async (_, _) =>
        {
            if (AppDialog.Show("Заменить внешнюю версию значениями из редактора? Обе версии сохраняются в резервных копиях.", "Внешние изменения", MessageBoxButton.YesNo, MessageBoxImage.Warning, this) != MessageBoxResult.Yes) return;
            try { await vm.KeepMineAndSaveAsync(); Close(); } catch (Exception e) { Status.Text = e.Message; }
        }));
        AddToolbar(selector); AddToolbar(actions); Body.Children.Add(grid);
        Status.Text = "Просмотрите сравнение. Перезагрузка сохраняет несохранённые правки в отдельную копию.";
        Loaded += (_, _) => selector.SelectedIndex = files.Count > 0 ? 0 : -1;
    }
}
public sealed class RecoveryConflictsWindow : WorkflowWindow
{
    public RecoveryConflictsWindow(MainViewModel vm) : base("Черновики, требующие проверки")
    {
        var grid = Table(vm.RecoveryConflicts, ("Файл", "FilePath"), ("Namespace", "Entry.Namespace"), ("Ключ", "Entry.Key"), ("Старый source", "Entry.Source"), ("Черновик", "Entry.Translation"), ("Причина", "Reason"));
        AddToolbar(ActionButton("Удалить выбранный черновик после проверки…", (_, _) =>
        {
            if (grid.SelectedItem is RecoveryConflict row && AppDialog.Show("Удалить эту запись восстановительного черновика после ручной проверки?", "Черновик", MessageBoxButton.YesNo, MessageBoxImage.Warning, this) == MessageBoxResult.Yes)
            { vm.RemoveRecoveryConflict(row); grid.ItemsSource = vm.RecoveryConflicts; }
        }));
        Body.Children.Add(grid); Status.Text = "Автоматическое восстановление заблокировано при несовпадении source. Черновики сохраняются между запусками до ручной проверки.";
    }
}