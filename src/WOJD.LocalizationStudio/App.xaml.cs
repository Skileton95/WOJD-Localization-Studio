using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WOJD.LocalizationStudio;

public partial class App : Application
{
    private static readonly string DiagnosticDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WOJD Localization Studio");

    private static readonly string DiagnosticPath = Path.Combine(
        DiagnosticDirectory,
        "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);
    }

    public static void WriteDiagnostic(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);

            var text =
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {source}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}" +
                $"{new string('-', 80)}{Environment.NewLine}";

            File.AppendAllText(
                DiagnosticPath,
                text,
                new UTF8Encoding(false));
        }
        catch
        {
            // Диагностика никогда не должна сама завершать приложение.
        }
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteDiagnostic(
            "DispatcherUnhandledException",
            e.Exception);

        // Нехватку памяти нельзя безопасно скрывать и продолжать работу.
        if (e.Exception is OutOfMemoryException)
            return;

        e.Handled = true;

        try
        {
            MessageBox.Show(
                MainWindow,
                "Произошла ошибка интерфейса, но приложение продолжит работу.\n\n" +
                e.Exception.Message +
                "\n\nПодробности сохранены в:\n" +
                DiagnosticPath,
                "WOJD Localization Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Не допускаем вторичного исключения в обработчике ошибки.
        }
    }

    private static void CurrentDomain_UnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteDiagnostic(
                "AppDomain.UnhandledException",
                exception);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteDiagnostic(
            "TaskScheduler.UnobservedTaskException",
            e.Exception);

        e.SetObserved();
    }
}
