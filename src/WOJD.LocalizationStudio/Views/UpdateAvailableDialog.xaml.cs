using System.Windows;

namespace WOJD.LocalizationStudio.Views;

public partial class UpdateAvailableDialog : Window
{
    public UpdateAvailableDialog(string version)
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Версия {version}";
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
