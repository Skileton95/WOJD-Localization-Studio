using System.Windows;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class TranslationHistoryWindow : Window
{
    public TranslationHistoryWindow(
        string fileName,
        string entryNamespace,
        string key,
        IReadOnlyList<TranslationHistoryRecord> records)
    {
        InitializeComponent();

        TitleText.Text = $"История: {key}";
        SubtitleText.Text = $"{fileName}  •  {entryNamespace}  •  записей: {records.Count:N0}";
        HistoryGrid.ItemsSource = records;
    }
}
