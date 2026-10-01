using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio;

public partial class SimilarityWindow : Window
{
    public SimilarityWindow(
        LocalizationEntry current,
        IReadOnlyList<SimilarityMatch> sourceMatches,
        IReadOnlyList<SimilarityMatch> translationMatches)
    {
        InitializeComponent();

        CurrentText.Text = $"Текущая строка: {current.Source}";
        SourceMatchesGrid.ItemsSource = sourceMatches;
        TranslationMatchesGrid.ItemsSource = translationMatches;
    }

    public string? SelectedTranslation { get; private set; }

    private void UseSourceMatchButton_Click(object sender, RoutedEventArgs e) =>
        UseSelectedSourceMatch();

    private void SourceMatchesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        UseSelectedSourceMatch();

    private void UseSelectedSourceMatch()
    {
        if (SourceMatchesGrid.SelectedItem is not SimilarityMatch match)
            return;

        SelectedTranslation = match.Entry.Translation;
        DialogResult = true;
    }
}
