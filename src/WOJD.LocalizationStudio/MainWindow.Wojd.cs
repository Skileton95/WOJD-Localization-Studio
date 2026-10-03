using System.Windows;
using WOJD.LocalizationStudio.Views;
namespace WOJD.LocalizationStudio;
public partial class MainWindow
{
    private void Glossary_Click(object sender, RoutedEventArgs e) { try { new GlossaryWindow(_viewModel) { Owner = this }.ShowDialog(); _viewModel.ReloadGlossary(); } catch (Exception ex) { WOJD.LocalizationStudio.Services.AppDialog.Show(ex.Message, "Глоссарий"); } }
    private void Migration_Click(object sender, RoutedEventArgs e) => new PatchMigrationWindow(_viewModel) { Owner = this }.ShowDialog();
    private void Collisions_Click(object sender, RoutedEventArgs e) { try { new CollisionWindow(_viewModel) { Owner = this }.ShowDialog(); } catch (Exception ex) { WOJD.LocalizationStudio.Services.AppDialog.Show(ex.Message, "Коллизии"); } }
    private void ImportWorkflow_Click(object sender, RoutedEventArgs e) => new ImportWorkflowWindow(_viewModel) { Owner = this }.ShowDialog();
    private void BinaryWorkflow_Click(object sender, RoutedEventArgs e) => new BinaryWorkflowWindow(_viewModel) { Owner = this }.ShowDialog();
    private void WojdProject_Click(object sender, RoutedEventArgs e) => new WojdProjectWindow(_viewModel) { Owner = this }.ShowDialog();
}
