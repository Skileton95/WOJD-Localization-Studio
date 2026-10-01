using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio;

public partial class GlossaryUsageWindow : Window
{
    private readonly Action<LocalizationDocument, LocalizationEntry>? _navigate;

    public GlossaryUsageWindow(
        GlossaryEntry term,
        IReadOnlyList<GlossaryUsageRow> rows,
        Action<LocalizationDocument, LocalizationEntry>? navigate)
    {
        InitializeComponent();

        _navigate = navigate;
        TitleText.Text = $"Где используется: {term.Source} → {term.Translation}";
        SummaryText.Text =
            $"Вхождений: {rows.Count:N0} • нарушений: {rows.Count(row => !row.IsAccepted):N0}";
        UsageGrid.ItemsSource = rows;
    }

    private void UsageGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UsageGrid.SelectedItem is not GlossaryUsageRow row)
            return;

        _navigate?.Invoke(row.Document, row.Entry);
        Close();
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
