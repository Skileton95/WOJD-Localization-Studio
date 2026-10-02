using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WOJD.LocalizationStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var window = new MainWindow();
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

        MessageBox.Show(
            $"{exception.GetType().Name}: {exception.Message}\n\nПолные сведения записаны в startup-error.log рядом с программой.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
