using System.Windows;
using System.Windows.Controls;

namespace WOJD.LocalizationStudio.Views;

public sealed class OperationProgressWindow : Window
{
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private readonly CancellationTokenSource _cts = new();
    private bool _allowClose;

    public CancellationToken CancellationToken => _cts.Token;

    public OperationProgressWindow(string title, string initialStatus)
    {
        Title = title;
        Width = 520;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _status = new TextBlock
        {
            Text = initialStatus,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(_status);

        _progress = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };
        Grid.SetRow(_progress, 1);
        root.Children.Add(_progress);

        var cancel = new Button
        {
            Content = "Отмена",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 16, 0, 0)
        };
        cancel.Click += (_, _) =>
        {
            cancel.IsEnabled = false;
            _status.Text = "Отмена операции…";
            _cts.Cancel();
        };
        Grid.SetRow(cancel, 2);
        root.Children.Add(cancel);

        Content = root;
        Closing += (_, e) =>
        {
            if (_allowClose)
                return;

            if (!_cts.IsCancellationRequested)
                _cts.Cancel();

            e.Cancel = true;
            _status.Text = "Отмена операции…";
        };
    }

    public void Report(string status, int processed = 0, int total = 0)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Report(status, processed, total));
            return;
        }

        _status.Text = status;

        if (total <= 0)
        {
            _progress.IsIndeterminate = true;
            return;
        }

        _progress.IsIndeterminate = false;
        _progress.Value = Math.Clamp(processed * 100.0 / total, 0, 100);
    }

    public void Complete()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Complete);
            return;
        }

        _allowClose = true;
        Close();
    }
}
