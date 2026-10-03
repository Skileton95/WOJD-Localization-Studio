using System.IO;
using System.Windows;
using System.Windows.Threading;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--self-check")
        {
            try { await DiagnosticsService.SelfCheckAsync(); Shutdown(0); }
            catch (Exception ex) { try { Directory.CreateDirectory(WorkspaceStateService.StorageDirectory); File.WriteAllText(Path.Combine(WorkspaceStateService.StorageDirectory, "self-check-error.log"), ex.ToString()); } catch { } Shutdown(1); }
            return;
        }

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
            var logPath = Path.Combine(WorkspaceStateService.StorageDirectory, "startup-error.log");
            Directory.CreateDirectory(WorkspaceStateService.StorageDirectory); File.WriteAllText(logPath, details);
        }
        catch
        {
            // Ошибка записи лога не должна скрывать исходную ошибку запуска.
        }

        AppDialog.Show(
            $"{exception.GetType().Name}: {exception.Message}\n\nПроверьте startup-error.log в папке workspace: {WorkspaceStateService.StorageDirectory}.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
