using System.Windows;
using WOJD.LocalizationStudio.Views;
namespace WOJD.LocalizationStudio;
public partial class MainWindow
{
    private void ImportWorkflow_Click(object sender, RoutedEventArgs e) => new ImportWorkflowWindow(_viewModel) { Owner = this }.ShowDialog();
    private void BinaryWorkflow_Click(object sender, RoutedEventArgs e) => new BinaryWorkflowWindow(_viewModel) { Owner = this }.ShowDialog();
    private void WojdProject_Click(object sender, RoutedEventArgs e) => new WojdProjectWindow(_viewModel) { Owner = this }.ShowDialog();
}
