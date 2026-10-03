using System.Windows;
using WOJD.LocalizationStudio.Views;
namespace WOJD.LocalizationStudio;
public partial class MainWindow
{
    private void WojdProject_Click(object sender, RoutedEventArgs e) => new WojdProjectWindow(_viewModel) { Owner = this }.ShowDialog();
}
