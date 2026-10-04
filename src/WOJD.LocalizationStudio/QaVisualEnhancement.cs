using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace WOJD.LocalizationStudio;

internal static class QaVisualEnhancement
{
    private static readonly ConditionalWeakTable<MainWindow, object>
        InstalledWindows = new();

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
            InstalledWindows.TryGetValue(window, out _))
        {
            return;
        }

        InstalledWindows.Add(window, new object());
        window.Dispatcher.BeginInvoke(
            () => Install(window),
            DispatcherPriority.ApplicationIdle);
    }

    private static void Install(MainWindow window)
    {
        if (window.FindName("EntriesGrid") is not DataGrid grid)
            return;

        var column = grid.Columns
            .OfType<DataGridTemplateColumn>()
            .FirstOrDefault(x =>
                string.Equals(
                    x.Header?.ToString(),
                    "QA",
                    StringComparison.Ordinal));

        if (column is null)
            return;

        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(
            TextBlock.TextProperty,
            new Binding("QaIndicator"));
        factory.SetBinding(
            FrameworkElement.ToolTipProperty,
            new Binding("ValidationSummary"));
        factory.SetBinding(
            TextBlock.ForegroundProperty,
            new Binding("QaIndicator")
            {
                Converter = new QaBrushConverter()
            });
        factory.SetValue(
            TextBlock.FontWeightProperty,
            FontWeights.Bold);
        factory.SetValue(
            TextBlock.FontSizeProperty,
            15.0);
        factory.SetValue(
            TextBlock.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        factory.SetValue(
            TextBlock.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        column.CellTemplate = new DataTemplate
        {
            VisualTree = factory
        };
    }

    private sealed class QaBrushConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
            => value?.ToString() switch
            {
                "✕" => new SolidColorBrush(Color.FromRgb(205, 55, 66)),
                "⚠" => new SolidColorBrush(Color.FromRgb(190, 126, 0)),
                "—" => new SolidColorBrush(Color.FromRgb(120, 128, 140)),
                "✓" => new SolidColorBrush(Color.FromRgb(32, 158, 98)),
                _ => Brushes.Transparent
            };

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
            => Binding.DoNothing;
    }
}
