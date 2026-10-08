using System.Windows;
using System.Windows.Controls;

namespace WOJD.LocalizationStudio.Views;

public partial class QaPage
{
    private bool _navigationHooked;
    private CheckBox? _familyOnlyCheckBox;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += QaPage_Loaded;
    }

    private void QaPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_navigationHooked)
        {
            IssueGrid.SelectionChanged += NavigationSelectionChanged;
            InstallFamilyOnlyToggle();
            _navigationHooked = true;
        }

        UpdateIssueNavigationState();
    }

    private void InstallFamilyOnlyToggle()
    {
        if (_familyOnlyCheckBox is not null || IssueTitleText.Parent is not Grid header)
            return;

        if (header.RowDefinitions.Count == 0)
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _familyOnlyCheckBox = new CheckBox
        {
            Content = "Только текущая группа ключей",
            Foreground = System.Windows.Media.Brushes.DimGray,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            ToolTip = "Показывать проблемы только для семейства текущего ключа, например 450_0."
        };
        _familyOnlyCheckBox.Checked += (_, _) => SetFamilyOnly(true);
        _familyOnlyCheckBox.Unchecked += (_, _) => SetFamilyOnly(false);
        Grid.SetRow(_familyOnlyCheckBox, 1);
        Grid.SetColumnSpan(_familyOnlyCheckBox, 2);
        header.Children.Add(_familyOnlyCheckBox);
    }

    private void NavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateIssueNavigationState();

    private void ApplyAndNext_Click(object sender, RoutedEventArgs e)
    {
        CommitQaTranslation();
        if (!_lastCommitRemovedCurrent)
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
        if (!_lastCommitRemovedCurrent)
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
