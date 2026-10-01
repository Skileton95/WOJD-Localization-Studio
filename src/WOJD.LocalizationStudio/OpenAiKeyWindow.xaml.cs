using System.Windows;

namespace WOJD.LocalizationStudio;

public partial class OpenAiKeyWindow : Window
{
    public OpenAiKeyWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApiKeyBox.Focus();
    }

    public string ApiKey => ApiKeyBox.Password;
    public bool Remember => RememberCheckBox.IsChecked == true;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            MessageBox.Show(
                this,
                "Введите API-ключ OpenAI.",
                "ChatGPT",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ApiKeyBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
