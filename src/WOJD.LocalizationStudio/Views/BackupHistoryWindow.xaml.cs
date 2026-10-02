using System.Windows;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class BackupHistoryWindow : Window
{
    private readonly MainViewModel _viewModel;

    public BackupHistoryWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        Refresh();
    }

    private void Refresh()
    {
        var document = _viewModel.ActiveDocument;

        if (document is null)
        {
            SubtitleText.Text = "Активный файл не выбран.";
            BackupsGrid.ItemsSource = null;
            return;
        }

        SubtitleText.Text =
            System.IO.Path.GetFileName(document.FilePath);

        BackupsGrid.ItemsSource =
            BackupService.ListBackups(document.FilePath);
    }

    private async void Restore_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupInfo backup ||
            _viewModel.ActiveDocument is not { } document)
        {
            return;
        }

        var result =
            AppDialog.Show(
                "Восстановить выбранную резервную копию? Текущая версия файла будет заменена.",
                "Восстановление backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                this);

        if (result != MessageBoxResult.Yes)
            return;

        BackupService.CreateBackup(document.FilePath);
        BackupService.RestoreBackup(
            backup.Path,
            document.FilePath);

        await _viewModel.ReloadActiveDocumentAsync();
        Refresh();
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
