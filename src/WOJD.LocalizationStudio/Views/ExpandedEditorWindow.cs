using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WOJD.LocalizationStudio.Controls;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class ExpandedEditorWindow : WorkflowWindow
{
    public ExpandedEditorWindow(MainViewModel vm) : base("Увеличенный редактор перевода")
    {
        DataContext = vm;
        var editor = new TokenTextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14), FontSize = vm.Settings.FontSize };
        editor.SetBinding(TextBox.TextProperty, new Binding("SelectedEntry.Translation") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        editor.SetBinding(TokenTextBox.SourceProperty, new Binding("SelectedEntry.Original"));
        var original = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), FontSize = vm.Settings.FontSize };
        original.SetBinding(TextBlock.TextProperty, new Binding("SelectedEntry.OriginalDisplay"));
        var wrapping = new CheckBox { Content = "Переносы", IsChecked = true, Margin = new Thickness(8) };
        wrapping.Checked += (_, _) => editor.TextWrapping = TextWrapping.Wrap; wrapping.Unchecked += (_, _) => editor.TextWrapping = TextWrapping.NoWrap;
        var actions = new WrapPanel(); actions.Children.Add(wrapping);
        actions.Children.Add(ActionButton("Применить и перейти дальше (Ctrl+Enter)", (_, _) => vm.ApplyCommand.Execute(null)));
        AddToolbar(actions);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new ScrollViewer { Content = original, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(editor, 1); grid.Children.Add(editor); Body.Children.Add(grid);
        Status.SetBinding(TextBlock.TextProperty, new Binding("SelectedEntry.ValidationSummary"));
        InputBindings.Add(new KeyBinding(vm.ApplyCommand, new KeyGesture(Key.Enter, ModifierKeys.Control)));
    }
}