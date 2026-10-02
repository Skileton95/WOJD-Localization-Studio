using System.Reflection;
using System.Windows;

namespace WOJD.LocalizationStudio.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version =
            Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString(3)
            ?? "0.0.0";

        VersionText.Text =
            $"Версия {version}";
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
