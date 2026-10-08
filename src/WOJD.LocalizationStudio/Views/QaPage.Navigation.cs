using System.Windows;
using System.Windows.Controls;

namespace WOJD.LocalizationStudio.Views;

public partial class QaPage
{
    private bool _navigationHooked;

    private void QaPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationHooked)
        {
            IssueGrid.SelectionChanged += NavigationSelectionChanged;
            _navigationHooked = true;
        }

        UpdateIssueNavigationState();
    }

    private void NavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateIssueNavigationState();

    private void ApplyAndNext_Click(object sender, RoutedEventArgs e)
    {
        CommitQaTranslation();
        MoveIssueSelection(1);
    }

    private void PreviousIssue_Click(object sender, RoutedEventArgs e)
    {
        CommitQaTranslation();
        MoveIssueSelection(-1);
    }

    private void NextIssue_Click(object sender, RoutedEventArgs e)
    {
        CommitQaTranslation();
        MoveIssueSelection(1);
    }

    private void MoveIssueSelection(int direction)
    {
        var count = IssueGrid.Items.Count;
        if (count == 0)
            return;

        var current = IssueGrid.SelectedIndex;
        var next = direction >= 0
            ? (current + 1 + count) % count
            : (current <= 0 ? count - 1 : current - 1);

        IssueGrid.SelectedIndex = next;
        if (IssueGrid.SelectedItem is not null)
            IssueGrid.ScrollIntoView(IssueGrid.SelectedItem);
        UpdateIssueNavigationState();
    }

    private void UpdateIssueNavigationState()
    {
        var count = IssueGrid.Items.Count;
        var hasItems = count > 0;
        ApplyTranslationButton.IsEnabled = hasItems;
        ApplyAndNextButton.IsEnabled = hasItems;
        PreviousIssueButton.IsEnabled = hasItems;
        NextIssueButton.IsEnabled = hasItems;
        OpenInEditorButton.IsEnabled = hasItems;

        IssuePositionText.Text = !hasItems || IssueGrid.SelectedIndex < 0
            ? string.Empty
            : $"{IssueGrid.SelectedIndex + 1:N0} / {count:N0}";
    }
}
