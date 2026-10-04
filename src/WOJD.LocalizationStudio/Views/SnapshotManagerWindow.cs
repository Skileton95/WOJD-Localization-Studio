using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Views;

public sealed class SnapshotManagerWindow : Window
{
    private readonly LocalizationDocument _document;
    private readonly DataGrid _grid;

    public bool Restored { get; private set; }

    public SnapshotManagerWindow(LocalizationDocument document)
    {
        _document = document;

        Title = "Снимки файла";
        Width = 820;
        Height = 500;
        MinWidth = 660;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = $"Снимки: {Path.GetFileName(document.FilePath)}",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(heading);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Создан",
            Binding = new System.Windows.Data.Binding("Created")
            {
                StringFormat = "yyyy-MM-dd HH:mm:ss"
            },
            Width = 160
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Причина",
            Binding = new System.Windows.Data.Binding("Label"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Строк",
            Binding = new System.Windows.Data.Binding("EntryCount")
            {
                StringFormat = "N0"
            },
            Width = 100
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Размер",
            Binding = new System.Windows.Data.Binding("Size")
            {
                StringFormat = "N0"
            },
            Width = 120
        });
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var create = new Button
        {
            Content = "Создать снимок",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        create.Click += (_, _) => CreateSnapshot();
        buttons.Children.Add(create);

        var restore = new Button
        {
            Content = "Восстановить",
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        restore.Click += (_, _) => RestoreSelected();
        buttons.Children.Add(restore);

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        buttons.Children.Add(close);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Refresh();
    }

    private void Refresh()
    {
        _grid.ItemsSource = SnapshotService.ListSnapshots(_document.FilePath);
    }

    private void CreateSnapshot()
    {
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            SnapshotService.CreateSnapshot(_document, "manual");
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ошибка создания снимка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void RestoreSelected()
    {
        if (_grid.SelectedItem is not SnapshotInfo snapshot)
        {
            MessageBox.Show(
                this,
                "Выберите снимок.",
                "Снимки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Вернуть переводы к снимку от {snapshot.Created:dd.MM.yyyy HH:mm:ss}?\n\nПеред восстановлением текущего состояния будет создан новый снимок.",
            "Восстановление снимка",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            SnapshotService.CreateSnapshot(_document, "before-snapshot-restore");
            var result = SnapshotService.RestoreSnapshot(_document, snapshot.Path);
            Restored = true;

            MessageBox.Show(
                this,
                result.Message,
                "Снимок восстановлен",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Ошибка восстановления снимка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
