using System.Windows;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio;

public partial class MassChangePreviewWindow : Window
{
    private readonly IReadOnlyList<ProposedTranslationChange> _changes;

    public MassChangePreviewWindow(
        string title,
        IReadOnlyList<ProposedTranslationChange> changes)
    {
        InitializeComponent();

        _changes = changes;
        TitleText.Text = title;
        ChangesGrid.ItemsSource = _changes;
        RefreshSummary();
    }

    public IReadOnlyList<ProposedTranslationChange> SelectedChanges =>
        _changes.Where(change => change.IsSelected).ToList();

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _changes)
            change.IsSelected = true;

        ChangesGrid.Items.Refresh();
        RefreshSummary();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var change in _changes)
            change.IsSelected = false;

        ChangesGrid.Items.Refresh();
        RefreshSummary();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ChangesGrid.CommitEdit();
        ChangesGrid.CommitEdit();

        if (SelectedChanges.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбрано ни одного изменения.",
                "Предпросмотр",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void RefreshSummary()
    {
        SummaryText.Text =
            $"Предложено изменений: {_changes.Count:N0} • выбрано: {_changes.Count(change => change.IsSelected):N0}";
    }
}
