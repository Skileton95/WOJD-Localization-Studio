using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class ProjectSearchWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _viewModel;

    private string _query = string.Empty;
    private bool _matchCase;
    private bool _exactMatch;
    private bool _useRegex;
    private string _resultStatus = "Введите запрос.";

    public ProjectSearchWindow(
        MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;

        foreach (var query in viewModel.ProjectSearchHistory)
            History.Add(query);

        DataContext = this;

        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BulkObservableCollection<ProjectSearchResult> Results { get; } = new();
    public ObservableCollection<string> History { get; } = new();

    public string Query
    {
        get => _query;
        set
        {
            if (_query == value)
                return;

            _query = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Query)));
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            if (_matchCase == value)
                return;

            _matchCase = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(MatchCase)));
        }
    }

    public bool ExactMatch
    {
        get => _exactMatch;
        set
        {
            if (_exactMatch == value)
                return;

            _exactMatch = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(ExactMatch)));
        }
    }

    public bool UseRegex
    {
        get => _useRegex;
        set
        {
            if (_useRegex == value)
                return;

            _useRegex = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(UseRegex)));
        }
    }

    public string ResultStatus
    {
        get => _resultStatus;
        private set
        {
            if (_resultStatus == value)
                return;

            _resultStatus = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(ResultStatus)));
        }
    }

    private async void Search_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            ResultStatus = "Введите запрос.";
            Results.ReplaceAll([]);
            return;
        }

        ResultStatus = "Поиск…";

        try
        {
            var response =
                await _viewModel.SearchProjectAsync(
                    Query,
                    MatchCase,
                    ExactMatch,
                    UseRegex);

            Results.ReplaceAll(response.Results);

            RefreshHistory();

            ResultStatus =
                response.IsTruncated
                    ? $"Найдено: {response.Results.Count:N0}+ (показаны первые 50 000)"
                    : $"Найдено: {response.Results.Count:N0}";
        }
        catch (Exception ex)
        {
            Results.ReplaceAll([]);
            ResultStatus = "Ошибка поиска";

            AppDialog.Show(
                ex.Message,
                "Ошибка поиска",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);
        }
    }

    private void RefreshHistory()
    {
        History.Clear();

        foreach (var query in _viewModel.ProjectSearchHistory)
            History.Add(query);
    }

    private void ResultsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        OpenSelected();
    }

    private void OpenSelected_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenSelected();
    }

    private void OpenSelected()
    {
        if (ResultsGrid.SelectedItem is not ProjectSearchResult result)
            return;

        _viewModel.OpenProjectSearchResult(result);
        DialogResult = true;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
