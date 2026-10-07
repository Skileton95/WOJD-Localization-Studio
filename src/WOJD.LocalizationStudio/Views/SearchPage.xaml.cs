using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class SearchPage : UserControl
{
    private MainViewModel? _viewModel;

    public event EventHandler? OpenInEditorRequested;

    public SearchPage()
    {
        InitializeComponent();
        StatusText.Text = "Введите запрос";
    }

    public void Attach(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void FocusSearch()
    {
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
        => await RunSearchAsync();

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(QueryBox.Text))
        {
            ResultsGrid.ItemsSource = null;
            StatusText.Text = "Введите запрос";
            return;
        }

        StatusText.Text = "Поиск…";
        try
        {
            var response = await _viewModel.SearchProjectAsync(
                QueryBox.Text,
                MatchCaseCheck.IsChecked == true,
                ExactCheck.IsChecked == true,
                RegexCheck.IsChecked == true);
            ResultsGrid.ItemsSource = response.Results;
            StatusText.Text = response.IsTruncated
                ? $"{response.Results.Count:N0}+ результатов"
                : $"{response.Results.Count:N0} результатов";
        }
        catch (Exception ex)
        {
            ResultsGrid.ItemsSource = null;
            StatusText.Text = "Ошибка";
            AppDialog.Show(
                ex.Message,
                "Ошибка поиска",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                Window.GetWindow(this));
        }
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || ResultsGrid.SelectedItem is not ProjectSearchResult result)
            return;

        _viewModel.OpenProjectSearchResult(result);
        OpenInEditorRequested?.Invoke(this, EventArgs.Empty);
    }
}
