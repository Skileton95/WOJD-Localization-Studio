using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio;

public partial class GlossaryUsageWindow : Window
{
    public GlossaryUsageWindow(
        GlossaryEntry term,
        IReadOnlyList<GlossaryUsageRow> rows)
    {
        InitializeComponent();

        TitleText.Text = $"Где используется: {term.Source} → {term.Translation}";
        SummaryText.Text =
            $"Вхождений: {rows.Count:N0} • нарушений: {rows.Count(row => !row.IsAccepted):N0}";
        UsageGrid.ItemsSource = rows;
    }

    public GlossaryUsageRow? SelectedUsage =>
        UsageGrid.SelectedItem as GlossaryUsageRow;

    private void UsageGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedUsage is not null)
            DialogResult = true;
    }
}

public sealed record GlossaryUsageRow(
    LocalizationDocument Document,
    LocalizationEntry Entry,
    bool IsAccepted)
{
    public string StatusText =>
        string.IsNullOrWhiteSpace(Entry.Translation)
            ? "Пусто"
            : IsAccepted ? "Верно" : "Нарушение";
}
