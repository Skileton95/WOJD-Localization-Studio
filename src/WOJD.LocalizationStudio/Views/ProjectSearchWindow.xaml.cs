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

    private CancellationTokenSource? _searchCancellation;
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
        Closed += (_, _) => _searchCancellation?.Cancel();

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

        _searchCancellation?.Cancel();
        var operation = new CancellationTokenSource(); _searchCancellation = operation;
        ResultStatus = "Поиск…";

        try
        {
            var response =
                await _viewModel.SearchProjectStreamingAsync(Query, MatchCase, ExactMatch, UseRegex, operation.Token,
                    new Progress<FileOperationProgress>(p => { if (ReferenceEquals(operation, _searchCancellation)) ResultStatus = $"{p.Stage} · {p.Percent:F0}%"; }));
            if (!ReferenceEquals(operation, _searchCancellation)) return;

            Results.ReplaceAll(response.Results);

            RefreshHistory();

            ResultStatus =
                response.IsTruncated
                    ? $"Найдено: {response.Results.Count:N0}+ (показаны первые 50 000)"
                    : $"Найдено: {response.Results.Count:N0}";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(operation, _searchCancellation)) ResultStatus = "Поиск отменён."; }
        catch (Exception ex)
        {
            if (!ReferenceEquals(operation, _searchCancellation)) return;
            Results.ReplaceAll([]);
            ResultStatus = "Ошибка поиска";

            AppDialog.Show(
                ex.Message,
                "Ошибка поиска",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                this);
        }
        finally { if (ReferenceEquals(operation, _searchCancellation)) _searchCancellation = null; operation.Dispose(); }
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

    private async void OpenSelected()
    {
        if (ResultsGrid.SelectedItem is not ProjectSearchResult result)
            return;

        try { await _viewModel.OpenProjectSearchResultAsync(result); DialogResult = true; } catch (Exception e) { ResultStatus = e.Message; }
    }

    private void CancelSearch_Click(object sender, RoutedEventArgs e) => _searchCancellation?.Cancel();

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
