using System.Windows;
using System.Windows.Input;

namespace WOJD.LocalizationStudio.Views;

public partial class TextInputDialog : Window
{
    public TextInputDialog(
        string title,
        string prompt,
        string initialValue = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ValueBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
