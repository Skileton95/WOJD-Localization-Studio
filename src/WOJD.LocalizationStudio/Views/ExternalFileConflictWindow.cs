using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public enum ExternalFileConflictDecision
{
    Cancel,
    Reload,
    Overwrite
}

public sealed class ExternalFileConflictWindow : Window
{
    private readonly LocalizationDocument _memory;
    private readonly LocalizationDocument _disk;
    private readonly TextBlock _summary;

    public ExternalFileConflictDecision Decision { get; private set; }
        = ExternalFileConflictDecision.Cancel;

    public ExternalFileConflictWindow(
        LocalizationDocument memory,
        LocalizationDocument disk,
        FileStamp openedStamp,
        FileStamp currentStamp)
    {
        _memory = memory;
        _disk = disk;

        Title = "Файл изменён на диске";
        Width = 760;
        Height = 400;
        MinWidth = 660;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Файл был изменён другой программой после открытия в редакторе.",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        _summary = new TextBlock
        {
            Text =
                $"Открыт: {openedStamp.LastWriteUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}  •  " +
                $"Сейчас: {currentStamp.LastWriteUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}\n" +
                "Сохранение поверх может затереть внешние изменения.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(_summary, 1);
        root.Children.Add(_summary);

        var info = new TextBlock
        {
            Text =
                "«Сравнить» покажет изменённые переводы. «Перезагрузить» отбросит локальные правки и возьмёт версию с диска, " +
                "но только если Namespace/Key/Original остались теми же.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 14)
        };
        Grid.SetRow(info, 2);
        root.Children.Add(info);

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var compare = MakeButton("Сравнить");
        compare.Click += (_, _) => ShowComparison();
        buttons.Children.Add(compare);

        var reload = MakeButton("Перезагрузить");
        reload.Click += (_, _) =>
        {
            if (!CanReloadInPlace(out var reason))
            {
                MessageBox.Show(
                    this,
                    reason + "\n\nЗакройте файл в редакторе и откройте его заново.",
                    "Нельзя перезагрузить автоматически",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var answer = MessageBox.Show(
                this,
                "Локальные несохранённые изменения будут потеряны. Перезагрузить версию с диска?",
                "Перезагрузка файла",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            Decision = ExternalFileConflictDecision.Reload;
            DialogResult = true;
        };
        buttons.Children.Add(reload);

        var overwrite = MakeButton("Сохранить поверх");
        overwrite.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                this,
                "Внешние изменения файла будут заменены текущей версией редактора. Продолжить?",
                "Сохранить поверх",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            Decision = ExternalFileConflictDecision.Overwrite;
            DialogResult = true;
        };
        buttons.Children.Add(overwrite);

        var cancel = MakeButton("Отмена");
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);

        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
    }

    private static Button MakeButton(string text)
        => new()
        {
            Content = text,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 110
        };

    private bool CanReloadInPlace(out string reason)
    {
        if (_memory.Entries.Count != _disk.Entries.Count)
        {
            reason = $"Количество строк изменилось: {_memory.Entries.Count:N0} → {_disk.Entries.Count:N0}.";
            return false;
        }

        for (var i = 0; i < _memory.Entries.Count; i++)
        {
            var a = _memory.Entries[i];
            var b = _disk.Entries[i];

            if (a.Index != b.Index ||
                !string.Equals(a.Namespace, b.Namespace, StringComparison.Ordinal) ||
                !string.Equals(a.Key, b.Key, StringComparison.Ordinal) ||
                !string.Equals(a.Original, b.Original, StringComparison.Ordinal))
            {
                reason = $"Структура файла изменилась около строки {i + 1:N0}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private void ShowComparison()
    {
        var diskByIdentity = _disk.Entries.ToDictionary(
            x => $"{x.Index}\u001F{x.Namespace}\u001F{x.Key}",
            StringComparer.Ordinal);

        var rows = _memory.Entries
            .Select(memory =>
            {
                diskByIdentity.TryGetValue(
                    $"{memory.Index}\u001F{memory.Namespace}\u001F{memory.Key}",
                    out var disk);

                return new CompareRow(
                    memory.Index,
                    memory.Namespace,
                    memory.Key,
                    memory.Translation,
                    disk?.Translation ?? "— строка отсутствует на диске —");
            })
            .Where(x => !string.Equals(x.InMemory, x.OnDisk, StringComparison.Ordinal))
            .ToList();

        var window = new Window
        {
            Title = "Сравнение с версией на диске",
            Width = 1100,
            Height = 650,
            MinWidth = 800,
            MinHeight = 450,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock
        {
            Text = $"Различающихся переводов: {rows.Count:N0}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding("Index"), Width = 60 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Namespace", Binding = new Binding("Namespace"), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Binding("Key"), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "В редакторе", Binding = new Binding("InMemory"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "На диске", Binding = new Binding("OnDisk"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        window.Content = root;
        window.ShowDialog();
    }

    private sealed record CompareRow(
        int Index,
        string Namespace,
        string Key,
        string InMemory,
        string OnDisk);
}
