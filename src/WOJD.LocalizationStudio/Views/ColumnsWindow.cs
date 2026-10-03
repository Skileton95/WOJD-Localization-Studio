using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.Views;
public sealed class ColumnsWindow : WorkflowWindow
{
    public ColumnsWindow(DataGrid table) : base("Столбцы таблицы")
    {
        var list = new StackPanel();
        foreach (var column in table.Columns)
        {
            var box = new CheckBox { Content = column.Header, IsChecked = column.Visibility == Visibility.Visible, Margin = new Thickness(8) };
            box.Checked += (_, _) => column.Visibility = Visibility.Visible;
            box.Unchecked += (_, _) => column.Visibility = Visibility.Collapsed;
            list.Children.Add(box);
        }
        AddToolbar(ActionButton("Сохранить", (_, _) => { var settings = AppSettingsService.Load(); settings.Columns = Capture(table); AppSettingsService.Save(settings); Close(); }));
        AddToolbar(new TextBlock { Text = "Ширину и порядок меняйте прямо в таблице; они запоминаются при закрытии приложения." });
        Body.Children.Add(list);
    }
    public static List<ColumnSetting> Capture(DataGrid grid) => grid.Columns.Select(c => new ColumnSetting(c.Header?.ToString() ?? "", c.Width.Value, c.Width.UnitType, c.DisplayIndex, c.Visibility == Visibility.Visible)).ToList();
    public static void Restore(DataGrid grid, IEnumerable<ColumnSetting> state)
    {
        foreach (var item in state.OrderBy(c => c.Order))
        {
            var column = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == item.Header);
            if (column is null || !double.IsFinite(item.Width) || item.Width <= 0) continue;
            column.Width = new DataGridLength(item.Width, item.Unit); column.Visibility = item.Visible ? Visibility.Visible : Visibility.Collapsed;
            column.DisplayIndex = Math.Clamp(item.Order, 0, grid.Columns.Count - 1);
        }
    }
}
