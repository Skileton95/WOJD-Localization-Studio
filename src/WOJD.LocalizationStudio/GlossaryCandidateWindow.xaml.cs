using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class GlossaryCandidateWindow : Window
{
    public GlossaryCandidateWindow(IReadOnlyList<GlossaryCandidate> candidates)
    {
        InitializeComponent();

        CandidateGrid.ItemsSource = candidates;
        SummaryText.Text =
            $"Кандидатов: {candidates.Count:N0}. Показаны повторяющиеся фразы с минимум 3 вхождениями.";
    }

    public GlossaryCandidate? SelectedCandidate =>
        CandidateGrid.SelectedItem as GlossaryCandidate;

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is null)
            return;

        DialogResult = true;
    }

    private void CandidateGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedCandidate is not null)
            DialogResult = true;
    }
}
