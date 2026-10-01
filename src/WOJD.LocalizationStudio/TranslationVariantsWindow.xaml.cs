using System.Windows;
using System.Windows.Input;

namespace WOJD.LocalizationStudio;

public partial class TranslationVariantsWindow : Window
{
    public TranslationVariantsWindow(string source, IReadOnlyList<string> variants)
    {
        InitializeComponent();

        SourceText.Text = $"Оригинал: {source}";
        VariantsList.ItemsSource = variants;
        if (variants.Count > 0)
            VariantsList.SelectedIndex = 0;
    }

    public string? SelectedTranslation => VariantsList.SelectedItem as string;

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTranslation is null)
            return;

        DialogResult = true;
    }

    private void VariantsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedTranslation is not null)
            DialogResult = true;
    }
}
