using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

internal static class FaqHelpRegistration
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
        if (sender is not MainWindow window || Installed.TryGetValue(window, out _))
            return;

        Installed.Add(window, new object());

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.F1)
                return;

            OpenFaq(window);
            args.Handled = true;
        };

        window.Dispatcher.BeginInvoke(() => AddHelpMenuItem(window));
    }

    private static void AddHelpMenuItem(MainWindow window)
    {
        var menu = FindVisualChild<Menu>(window);
        if (menu is null)
            return;

        var help = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(x =>
                string.Equals(x.Header?.ToString(), "Справка", StringComparison.Ordinal));

        if (help is null)
            return;

        if (help.Items.OfType<MenuItem>().Any(x => x.Tag?.ToString() == "faq-help"))
            return;

        var faq = new MenuItem
        {
            Header = "FAQ и справка…",
            InputGestureText = "F1",
            Tag = "faq-help"
        };
        faq.Click += (_, _) => OpenFaq(window);

        if (help.Items.Count > 0)
            help.Items.Insert(0, new Separator());
        help.Items.Insert(0, faq);
    }

    private static void OpenFaq(MainWindow owner)
    {
        var dialog = new FaqHelpWindowV2
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
