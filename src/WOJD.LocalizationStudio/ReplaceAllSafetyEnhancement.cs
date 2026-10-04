using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio;

internal static class ReplaceAllSafetyEnhancement
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

        var replaceAll = FindVisualChildren<Button>(window)
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "Заменить всё", StringComparison.Ordinal));

        if (replaceAll?.Command is not ICommand originalCommand)
            return;

        Installed.Add(window, new object());
        replaceAll.Command =
            new SnapshotGuardCommand(
                window,
                viewModel,
                originalCommand,
                replaceAll.CommandParameter);
    }

    private sealed class SnapshotGuardCommand(
        MainWindow window,
        MainViewModel viewModel,
        ICommand inner,
        object? parameter)
        : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => inner.CanExecuteChanged += value;
            remove => inner.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? value)
            => inner.CanExecute(parameter ?? value);

        public void Execute(object? value)
        {
            var document = viewModel.ActiveDocument;

            if (document is not null &&
                !string.IsNullOrEmpty(viewModel.SearchText))
            {
                try
                {
                    SnapshotService.CreateSnapshot(document, "before-replace-all");
                    ProjectHistoryService.Record(
                        document.FilePath,
                        "Подготовка массовой замены",
                        0,
                        $"Поиск: «{viewModel.SearchText}» → «{viewModel.ReplaceText}»");
                }
                catch (Exception ex)
                {
                    AppDialog.Show(
                        "Массовая замена отменена: не удалось создать защитный снимок.\n\n" + ex.Message,
                        "Защита массовой замены",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error,
                        window);
                    return;
                }
            }

            inner.Execute(parameter ?? value);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
