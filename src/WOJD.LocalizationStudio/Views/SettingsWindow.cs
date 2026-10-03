using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.Views;
public sealed class SettingsWindow : WorkflowWindow
{
    public SettingsWindow() : base("Настройки")
    {
        var settings = AppSettingsService.Load();
        var font = new TextBox { Text = settings.FontSize.ToString(), Width = 70 };
        var debounce = new TextBox { Text = settings.SearchDebounceMs.ToString(), Width = 70 };
        var backups = new TextBox { Text = settings.BackupLimit.ToString(), Width = 70 };
        var fields = new WrapPanel();
        foreach (var pair in new[] { ("Шрифт", font), ("Поиск, мс", debounce), ("Лимит копий", backups) })
        { fields.Children.Add(new TextBlock { Text = pair.Item1, Margin = new Thickness(8) }); fields.Children.Add(pair.Item2); }
        AddToolbar(fields);
        var grid = Table(settings.Shortcuts, ("Команда", "Action"), ("Клавиши", "Gesture"));
        grid.IsReadOnly = false; grid.Columns[0].IsReadOnly = true;
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Сохранить", (_, _) =>
        {
            try
            {
                grid.CommitEdit(); grid.CommitEdit();
                settings.FontSize = double.Parse(font.Text); settings.SearchDebounceMs = int.Parse(debounce.Text); settings.BackupLimit = int.Parse(backups.Text);
                AppSettingsService.Save(settings); DialogResult = true;
            }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        actions.Children.Add(ActionButton("Сбросить настройки", (_, _) => { AppSettingsService.Save(new()); DialogResult = true; }));
        AddToolbar(actions); Body.Children.Add(grid);
        Status.Text = "Пустая комбинация отключает команду. Пример: Ctrl+Shift+S. Ctrl+Tab, Ctrl+W и команды поиска пока сохраняют фиксированные комбинации.";
    }
}
public sealed class ReportWindow : WorkflowWindow
{
    public ReportWindow(string title, object rows, params (string Header, string Property)[] columns) : base(title) => Body.Children.Add(Table(rows, columns));
}