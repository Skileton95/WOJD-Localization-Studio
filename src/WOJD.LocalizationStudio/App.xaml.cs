using System.IO;
using System.Windows;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var window = new ShellWindow();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            ShowFatalError("Ошибка запуска редактора", ex);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError("Необработанная ошибка", e.Exception);
        e.Handled = true;
    }

    private static void ShowFatalError(string title, Exception exception)
    {
        var details = exception.ToString();

        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            File.WriteAllText(logPath, details);
        }
        catch
        {
            // Ошибка записи лога не должна скрывать исходную ошибку запуска.
        }

        AppDialog.Show(
            $"{exception.GetType().Name}: {exception.Message}\n\nПолные сведения записаны в startup-error.log рядом с программой.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
