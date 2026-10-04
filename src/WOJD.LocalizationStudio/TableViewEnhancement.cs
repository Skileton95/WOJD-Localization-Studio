using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

internal static class TableViewEnhancement
{
    private static readonly ConditionalWeakTable<MainWindow, object> Installed = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window ||
            Installed.TryGetValue(window, out _) ||
            window.FindName("EntriesGrid") is not DataGrid grid)
        {
            return;
        }

        Installed.Add(window, new object());
        Apply(grid, EditorSettingsService.Current);

        var menu = grid.ContextMenu ?? new ContextMenu();
        grid.ContextMenu = menu;
        menu.Items.Add(new Separator());

        var view = new MenuItem { Header = "Вид таблицы" };
        view.Items.Add(CreateColumnToggle(grid, "Namespace", "Namespace", s => s.ShowNamespaceColumn, (s, v) => s.ShowNamespaceColumn = v));
        view.Items.Add(CreateColumnToggle(grid, "Ключ", "Ключ", s => s.ShowKeyColumn, (s, v) => s.ShowKeyColumn = v));
        view.Items.Add(CreateColumnToggle(grid, "Статус", "Статус", s => s.ShowStatusColumn, (s, v) => s.ShowStatusColumn = v));
        view.Items.Add(new Separator());

        var compact = new MenuItem
        {
            Header = "Компактные строки",
            IsCheckable = true,
            IsChecked = EditorSettingsService.Current.CompactTable
        };
        compact.Click += (_, _) =>
        {
            var settings = EditorSettingsService.Current;
            settings.CompactTable = compact.IsChecked;
            EditorSettingsService.Save(settings);
            Apply(grid, settings);
        };
        view.Items.Add(compact);
        menu.Items.Add(view);
    }

    private static MenuItem CreateColumnToggle(
        DataGrid grid,
        string header,
        string display,
        Func<EditorSettings, bool> getter,
        Action<EditorSettings, bool> setter)
    {
        var item = new MenuItem
        {
            Header = display,
            IsCheckable = true,
            IsChecked = getter(EditorSettingsService.Current)
        };

        item.Click += (_, _) =>
        {
            var settings = EditorSettingsService.Current;
            setter(settings, item.IsChecked);
            EditorSettingsService.Save(settings);
            Apply(grid, settings);
        };

        return item;
    }

    private static void Apply(DataGrid grid, EditorSettings settings)
    {
        SetColumn(grid, "Namespace", settings.ShowNamespaceColumn);
        SetColumn(grid, "Ключ", settings.ShowKeyColumn);
        SetColumn(grid, "Статус", settings.ShowStatusColumn);
        grid.RowHeight = settings.CompactTable ? 25 : double.NaN;
        grid.ColumnHeaderHeight = settings.CompactTable ? 28 : double.NaN;
    }

    private static void SetColumn(DataGrid grid, string header, bool visible)
    {
        var column = grid.Columns.FirstOrDefault(
            x => string.Equals(x.Header?.ToString(), header, StringComparison.Ordinal));

        if (column is not null)
            column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
