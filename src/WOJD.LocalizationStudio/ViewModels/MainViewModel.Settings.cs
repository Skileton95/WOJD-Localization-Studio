using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _operationCancellation;
    private double _busyPercent;
    public EditorSettings Settings { get; private set; } = AppSettingsService.Load();
    public double BusyPercent { get => _busyPercent; private set => SetProperty(ref _busyPercent, value); }
    public void CancelOperation() => _operationCancellation?.Cancel();
    public void ApplySettings(EditorSettings settings)
    {
        Settings = settings; _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(settings.SearchDebounceMs); OnPropertyChanged(nameof(Settings));
    }
    private IProgress<FileOperationProgress> OperationProgress()
    {
        var operation = _operationCancellation;
        return new Progress<FileOperationProgress>(p =>
        {
            if (!ReferenceEquals(operation, _operationCancellation) || !IsBusy) return;
            BusyPercent = p.Percent; BusyText = $"{p.Stage}: {p.Rows:N0} строк · {p.Percent:F0}%";
        });
    }
}