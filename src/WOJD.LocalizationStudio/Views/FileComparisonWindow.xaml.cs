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
            $"Добавлено: {_result.Added}    Удалено: {_result.Removed}    Изменён оригинал: {_result.OriginalChanged}    Изменён перевод: {_result.TranslationChanged}";

        WarningText.Text =
            _result.OriginalChanged > 0
                ? $"Внимание: {_result.OriginalChanged} строк с изменённым оригиналом. Их переводы не будут перенесены автоматически."
                : "Изменений оригинального текста не обнаружено.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileComparisonItem> Items { get; }
    public ICollectionView ItemsView { get; }

    public string[] Filters { get; } =
    [
        "Все изменения",
        "Добавлено",
        "Удалено",
        "Изменён оригинал",
        "Изменён перевод"
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
            $"Перенесено переводов: {result.Transferred}.\nПропущено из-за изменённого оригинала: {result.SkippedChangedSource}.",
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
