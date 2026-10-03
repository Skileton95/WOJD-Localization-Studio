using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;
namespace WOJD.LocalizationStudio.Views;
public sealed class EditHistoryWindow : WorkflowWindow
{
    public EditHistoryWindow(MainViewModel vm) : base("История правок сессии")
    {
        var history = Table(vm.EditHistory, ("Дата UTC", "AtUtc"), ("Автор", "Author"), ("Операция", "Operation"), ("Строк", "Count"));
        var changes = Table(null!, ("Файл", "FilePath"), ("Namespace", "Namespace"), ("Ключ", "Key"), ("Поле", "Field"), ("До", "Before"), ("После", "After"));
        history.SelectionChanged += (_, _) => { if (history.SelectedItem is EditHistoryItem row) changes.ItemsSource = row.Rows; };
        AddToolbar(ActionButton("Восстановить «До» выбранной операции", (_, _) =>
        {
            if (history.SelectedItem is not EditHistoryItem row) return;
            try { vm.RevertHistory(row); Status.Text = "Значения восстановлены одной операцией; доступны Undo и Redo."; }
            catch (Exception e) { Status.Text = e.Message; }
        }));
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(history); Grid.SetRow(changes, 1); grid.Children.Add(changes); Body.Children.Add(grid);
        Status.Text = "Последние 2000 операций текущей сессии. Несколько файлов одной массовой операции отображаются одной записью.";
    }
}