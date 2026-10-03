using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Views;
namespace WOJD.LocalizationStudio;
public partial class MainWindow
{
    private void Columns_Click(object sender, RoutedEventArgs e) => new ColumnsWindow(EntriesGrid) { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => AppDialog.Show(
        $"WOJD Localization Studio {_viewModel.AppVersion}\n\nСостояние проекта: {WorkspaceStateService.StorageDirectory}\nДиагностика: problems.log\nИстория защиты: safety.jsonl\n\nРелизы и changelog: github.com/Skileton95/WOJD-Localization-Studio/releases", "О программе", owner: this);
    private void AddWindowShortcuts(Dictionary<string, ICommand> commands)
    {
        commands["Search"] = new RelayCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
        commands["ProjectSearch"] = new RelayCommand(ShowProjectSearchWindow);
        commands["GoTo"] = new RelayCommand(ShowGoToDialog);
        commands["NextTab"] = new RelayCommand(() => _viewModel.CycleTab(1));
        commands["PreviousTab"] = new RelayCommand(() => _viewModel.CycleTab(-1));
        commands["CloseTab"] = new RelayCommand(() => { _ = _viewModel.CloseCurrentFileAsync(); });
    }
    private void UpdateMenuGestures(System.Windows.Controls.ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (item.Command is not null)
                item.InputGestureText = (InputBindings.OfType<KeyBinding>().FirstOrDefault(b => ReferenceEquals(b.Command, item.Command))?.Gesture as KeyGesture)?.GetDisplayStringForCulture(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            else if (item.Header?.ToString() is string header)
            {
                var action = header.StartsWith("Поиск по проекту") ? "ProjectSearch" : header.StartsWith("Перейти к строке") ? "GoTo" : null;
                if (action is not null) item.InputGestureText = _viewModel.Settings.Shortcuts.FirstOrDefault(s => s.Action == action)?.Gesture ?? "";
            }
            UpdateMenuGestures(item);
        }
    }
}
