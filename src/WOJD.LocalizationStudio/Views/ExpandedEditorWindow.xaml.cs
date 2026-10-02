using System.Windows;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Views;

public partial class ExpandedEditorWindow : Window
{
    public ExpandedEditorWindow(
        LocalizationEntry entry)
    {
        InitializeComponent();
        DataContext = entry;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
