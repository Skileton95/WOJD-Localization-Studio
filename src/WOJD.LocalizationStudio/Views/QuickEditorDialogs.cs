using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

internal sealed class FindReplaceWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TextBox _findBox = new();
    private readonly TextBox _replaceBox = new();

    public FindReplaceWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "Найти и заменить";
        Width = 520;
        Height = 245;
        MinWidth = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        _findBox.Text = viewModel.SearchText;
        _replaceBox.Text = viewModel.ReplaceText;
        _findBox.Padding = new Thickness(8, 6, 8, 6);
        _replaceBox.Padding = new Thickness(8, 6, 8, 6);

        var panel = new Grid { Margin = new Thickness(18) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var findLabel = new TextBlock { Text = "Найти", FontWeight = FontWeights.SemiBold };
        panel.Children.Add(findLabel);
        Grid.SetRow(_findBox, 1);
        _findBox.Margin = new Thickness(0, 6, 0, 12);
        panel.Children.Add(_findBox);

        var replaceLabel = new TextBlock { Text = "Заменить на", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(replaceLabel, 2);
        panel.Children.Add(replaceLabel);
        Grid.SetRow(_replaceBox, 3);
        _replaceBox.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_replaceBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var current = CreateButton("Заменить текущую");
        current.Click += (_, _) => Execute(false);
        var all = CreateButton("Заменить все");
        all.Click += (_, _) => Execute(true);
        var close = CreateButton("Закрыть");
        close.Click += (_, _) => Close();
        buttons.Children.Add(current);
        buttons.Children.Add(all);
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(panel);
        Content = root;

        Loaded += (_, _) =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void Execute(bool all)
    {
        _viewModel.SearchText = _findBox.Text;
        _viewModel.ReplaceText = _replaceBox.Text;
        var command = all ? _viewModel.ReplaceAllCommand : _viewModel.ReplaceCurrentCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private static Button CreateButton(string text)
        => new()
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 90
        };
}

internal sealed class JumpToEntryWindow : Window
{
    private readonly LocalizationDocument _document;
    private readonly TextBox _queryBox = new();
    private readonly TextBlock _status = new();

    public LocalizationEntry? SelectedEntry { get; private set; }

    public JumpToEntryWindow(LocalizationDocument document)
    {
        _document = document;
        Title = "Перейти к строке или ключу";
        Width = 520;
        Height = 205;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock
        {
            Text = "Введите номер строки, точный Key или Namespace / Key",
            FontWeight = FontWeights.SemiBold
        });
        _queryBox.Margin = new Thickness(0, 8, 0, 6);
        _queryBox.Padding = new Thickness(8, 6, 8, 6);
        _queryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                FindAndClose();
                e.Handled = true;
            }
        };
        stack.Children.Add(_queryBox);
        _status.Foreground = System.Windows.Media.Brushes.DimGray;
        stack.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var go = new Button { Content = "Перейти", Padding = new Thickness(14, 7, 14, 7), MinWidth = 90 };
        go.Click += (_, _) => FindAndClose();
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(14, 7, 14, 7), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(go);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) => _queryBox.Focus();
    }

    private void FindAndClose()
    {
        var query = _queryBox.Text.Trim();
        if (query.Length == 0)
            return;

        LocalizationEntry? entry = null;
        if (int.TryParse(query, out var index))
        {
            entry = _document.Entries.FirstOrDefault(x => x.Index == index);
            if (entry is null && index >= 1 && index <= _document.Entries.Count)
                entry = _document.Entries[index - 1];
        }

        entry ??= _document.Entries.FirstOrDefault(x =>
            string.Equals(x.Key, query, StringComparison.OrdinalIgnoreCase));
        entry ??= _document.Entries.FirstOrDefault(x =>
            string.Equals($"{x.Namespace} / {x.Key}", query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{x.Namespace}.{x.Key}", query, StringComparison.OrdinalIgnoreCase));
        entry ??= _document.Entries.FirstOrDefault(x =>
            x.Key.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _status.Text = "Строка не найдена.";
            return;
        }

        SelectedEntry = entry;
        DialogResult = true;
        Close();
    }
}
