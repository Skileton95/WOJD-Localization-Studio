using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class ProjectHistoryRegistration
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
            window.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        Installed.Add(window, new object());

        void RegisterCurrent()
        {
            if (viewModel.ActiveDocument is not null)
                ProjectHistoryService.RegisterDocument(viewModel.ActiveDocument);
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedEntry))
                RegisterCurrent();
        };

        viewModel.PropertyChanged += handler;
        RegisterCurrent();

        window.Closed += (_, _) =>
            viewModel.PropertyChanged -= handler;
    }
}
