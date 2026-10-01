using System.Windows;

namespace WOJD.LocalizationStudio;

public partial class AiCorrectionPreviewWindow : Window
{
    public AiCorrectionPreviewWindow(
        string source,
        string before,
        string after,
        string validationSummary)
    {
        InitializeComponent();

        SourceText.Text = $"Оригинал: {source}";
        BeforeTextBox.Text = before;
        AfterTextBox.Text = after;
        ValidationText.Text = string.IsNullOrWhiteSpace(validationSummary)
            ? "✓ Исправленный вариант проходит проверку."
            : $"⚠ {validationSummary}";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
