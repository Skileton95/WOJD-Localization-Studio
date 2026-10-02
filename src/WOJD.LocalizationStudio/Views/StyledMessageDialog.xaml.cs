using System.Windows;
using System.Windows.Input;

namespace WOJD.LocalizationStudio.Views;

public partial class StyledMessageDialog : Window
{
    private readonly MessageBoxButton _buttons;

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public StyledMessageDialog(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        InitializeComponent();

        _buttons = buttons;
        TitleText.Text = title;
        MessageText.Text = message;

        ConfigureIcon(image);
        ConfigureButtons(buttons);
    }

    private void ConfigureIcon(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Error:
                IconText.Text = "×";
                IconText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(220, 68, 76));
                IconBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 237, 239));
                break;

            case MessageBoxImage.Warning:
                IconText.Text = "!";
                IconText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(230, 154, 29));
                IconBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 247, 225));
                break;

            default:
                IconText.Text = "i";
                break;
        }
    }

    private void ConfigureButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                SecondaryButton.Content = "Нет";
                SecondaryButton.Visibility = Visibility.Visible;
                PrimaryButton.Content = "Да";
                break;

            case MessageBoxButton.OKCancel:
                SecondaryButton.Content = "Отмена";
                SecondaryButton.Visibility = Visibility.Visible;
                PrimaryButton.Content = "ОК";
                break;

            default:
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "ОК";
                break;
        }
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            _ => MessageBoxResult.OK
        };

        DialogResult = true;
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.Cancel
        };

        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == MessageBoxButton.YesNo
            ? MessageBoxResult.No
            : MessageBoxResult.Cancel;

        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
