using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public partial class FileComparisonWindow : Window, INotifyPropertyChanged
{
    private readonly FileComparisonResult _result;
    private string _selectedFilter = "Все изменения";

    public FileComparisonWindow(
        LocalizationDocument current,
        LocalizationDocument oldDocument,
        string oldFileName)
    {
        InitializeComponent();

        _result =
            FileComparisonService.Compare(
                current,
                oldDocument);

        Items =
            new ObservableCollection<FileComparisonItem>(
                _result.Items);

        ItemsView =
            CollectionViewSource.GetDefaultView(Items);

        ItemsView.Filter =
            item =>
                item is FileComparisonItem row &&
                (SelectedFilter == "Все изменения" ||
                 row.ChangeType == SelectedFilter);

        DataContext = this;

        SubtitleText.Text =
            $"Текущий: {System.IO.Path.GetFileName(current.FilePath)}  ←  старая версия: {oldFileName}";

        SummaryText.Text =
            $"Новые: {_result.Added}    Удалено: {_result.Removed}    Можно перенести: {_result.Transferable}    Проверить вручную: {_result.NeedsReview}";

        WarningText.Text =
            _result.NeedsReview > 0
                ? $"Внимание: {_result.NeedsReview} строк требуют ручной проверки и не будут перезаписаны автоматически."
                : "Конфликтов, требующих ручной проверки, не обнаружено.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileComparisonItem> Items { get; }
    public ICollectionView ItemsView { get; }

    public string[] Filters { get; } =
    [
        "Все изменения",
        "Новая строка",
        "Перевод можно перенести",
        "Нужно проверить вручную",
        "Изменён перевод",
        "Удалено"
    ];

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter == value)
                return;

            _selectedFilter = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedFilter)));

            ItemsView.Refresh();
        }
    }

    private void Transfer_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            FileComparisonService.TransferTranslations(
                Items);

        AppDialog.Show(
            $"Безопасно перенесено переводов: {result.Transferred}.\n" +
            $"Пропущено из-за изменённого оригинала: {result.SkippedChangedSource}.\n" +
            $"Не перезаписано существующих переводов: {result.SkippedExistingTranslation}.",
            "Перенос переводов",
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            this);

        Close();
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
