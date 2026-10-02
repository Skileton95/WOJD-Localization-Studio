using System.Windows;
using WOJD.LocalizationStudio.Views;

namespace WOJD.LocalizationStudio.Services;

public static class AppDialog
{
    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information,
        Window? owner = null)
    {
        var dialog = new StyledMessageDialog(
            message,
            title,
            buttons,
            image);

        owner ??= Application.Current?.MainWindow;

        if (owner is not null &&
            owner.IsVisible &&
            owner != dialog)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation =
                WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }
}
