using System.Windows;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio;

public partial class GlossaryConsistencyWindow : Window
{
    public GlossaryConsistencyWindow(IReadOnlyList<GlossaryConsistencyRow> rows)
    {
        InitializeComponent();

        ConsistencyGrid.ItemsSource = rows;

        var totalHits = rows.Sum(row => row.TotalOccurrences);
        var mismatches = rows.Sum(row => row.MismatchOccurrences);
        SummaryText.Text =
            $"Закреплённых терминов в файле: {rows.Count:N0} • вхождений: {totalHits:N0} • нарушений: {mismatches:N0}";
    }
}
