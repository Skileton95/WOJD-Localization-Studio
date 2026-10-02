using System.Windows;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class ProjectReplaceWindow : Window
{
    private readonly MainViewModel _viewModel;
    private List<ProjectReplaceCandidate> _candidates = [];

    public ProjectReplaceWindow(
        MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        QueryBox.Focus();
    }

    private void Preview_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _candidates =
                ProjectReplaceService.Preview(
                    _viewModel.GetOpenDocuments(),
                    QueryBox.Text,
                    ReplacementBox.Text,
                    MatchCaseBox.IsChecked == true,
                    WholeWordBox.IsChecked == true,
                    RegexBox.IsChecked == true);

            PreviewGrid.ItemsSource =
                _candidates;

            StatusText.Text =
                $"Будет изменено строк: {_candidates.Count:N0}.";
        }
        catch (Exception ex)
        {
            _candidates = [];
            PreviewGrid.ItemsSource = null;
            StatusText.Text = "Ошибка.";

            AppDialog.Show(
                ex.Message,
                "Ошибка массовой замены",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);
        }
    }

    private void Apply_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selected =
            _candidates
                .Where(x => x.IsSelected)
                .ToList();

        if (selected.Count == 0)
            return;

        var confirmation =
            AppDialog.Show(
                $"Применить изменения к строкам: {selected.Count:N0}?",
                "Массовая замена",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                this);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _viewModel.ApplyProjectReplacements(
            selected);

        StatusText.Text =
            $"Применено строк: {selected.Count:N0}.";

        DialogResult = true;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
