using System.Windows;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class GlossaryConflictWindow : Window
{
    public GlossaryConflictWindow(IReadOnlyList<GlossaryConflict> conflicts)
    {
        InitializeComponent();

        ConflictGrid.ItemsSource = conflicts;
        SummaryText.Text = conflicts.Count == 0
            ? "Конфликтов не найдено."
            : $"Найдено конфликтов: {conflicts.Count:N0}";
    }
}
