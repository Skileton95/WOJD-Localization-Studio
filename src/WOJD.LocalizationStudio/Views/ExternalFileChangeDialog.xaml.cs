using System.Windows;
using System.Windows.Input;

namespace WOJD.LocalizationStudio.Views;

public enum ExternalFileChangeAction
{
    Cancel,
    Reload,
    Compare,
    KeepMine
}

public partial class ExternalFileChangeDialog : Window
{
    public ExternalFileChangeDialog(string fileName)
    {
        InitializeComponent();

        MessageText.Text =
            $"Файл «{fileName}» был изменён другой программой после открытия в WOJD Localization Studio.";
    }

    public ExternalFileChangeAction Action { get; private set; }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        Action = ExternalFileChangeAction.Reload;
        DialogResult = true;
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        Action = ExternalFileChangeAction.Compare;
        DialogResult = true;
    }

    private void KeepMine_Click(object sender, RoutedEventArgs e)
    {
        Action = ExternalFileChangeAction.KeepMine;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Action = ExternalFileChangeAction.Cancel;
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
