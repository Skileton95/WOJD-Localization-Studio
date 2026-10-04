using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class BulkSnapshotEnhancement
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
        if (window.DataContext is not MainViewModel viewModel)
            return;

        var button = FindVisualChildren<Button>(window)
            .FirstOrDefault(x =>
                string.Equals(
                    x.Content?.ToString(),
                    "Массово…",
                    StringComparison.Ordinal));

        if (button is null)
            return;

        button.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (viewModel.ActiveDocument is not LocalizationDocument document)
                return;

            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                SnapshotService.CreateSnapshot(
                    document,
                    "before-bulk-autocorrect");
            }
            catch (Exception ex)
            {
                args.Handled = true;
                AppDialog.Show(
                    "Массовая операция отменена: не удалось создать защитный снимок.\n\n" + ex.Message,
                    "Снимок перед массовой операцией",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    window);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
