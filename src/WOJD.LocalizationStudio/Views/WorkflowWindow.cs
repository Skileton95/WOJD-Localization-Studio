using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace WOJD.LocalizationStudio.Views;

public class WorkflowWindow : Window
{
    protected DockPanel Body { get; } = new() { Margin = new Thickness(18) };
    protected TextBlock Status { get; } = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    public WorkflowWindow(string title)
    {
        Title = title; Width = 1100; Height = 720; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        Content = Body;
        DockPanel.SetDock(Status, Dock.Bottom); Body.Children.Add(Status);
    }
    protected void AddToolbar(UIElement element) { DockPanel.SetDock(element, Dock.Top); Body.Children.Add(element); }
    protected Button ActionButton(string label, RoutedEventHandler action)
    {
        var button = new Button { Content = label, Padding = new Thickness(12,6,12,6), Margin = new Thickness(0,0,8,8) };
        if (TryFindResource("SecondaryButton") is Style style) button.Style = style;
        button.Click += action; return button;
    }
    public static DataGrid Table(object source, params (string Header, string Property)[] columns)
    {
        var grid = new DataGrid { ItemsSource = source as System.Collections.IEnumerable, AutoGenerateColumns = false,
            IsReadOnly = true, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single };
        foreach (var (header, property) in columns)
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }
}