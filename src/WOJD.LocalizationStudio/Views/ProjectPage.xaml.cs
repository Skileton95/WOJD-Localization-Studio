using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class ProjectPage : UserControl
{
    private MainViewModel? _viewModel;

    public ProjectPage()
    {
        InitializeComponent();
    }

    public void Attach(MainViewModel viewModel)
    {
        if (!ReferenceEquals(_viewModel, viewModel))
        {
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = viewModel;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        DataContext = viewModel;
        Refresh();
    }

    public void Detach()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    public void Refresh()
    {
        var document = _viewModel?.ActiveDocument;
        ActiveFileNameText.Text = document is null ? "Файл не открыт" : Path.GetFileName(document.FilePath);
        ActiveFilePathText.Text = document?.FilePath ?? string.Empty;
        EntryCountText.Text = document is null ? "0" : $"{document.Entries.Count:N0}";
        ModifiedCountText.Text = _viewModel is null ? "0" : $"{_viewModel.ModifiedCount:N0}";
        UnsavedText.Text = _viewModel?.HasUnsavedChanges == true
            ? "Есть несохранённые изменения."
            : "Все изменения сохранены.";
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.ModifiedCount)
            or nameof(MainViewModel.SelectedEntry))
        {
            Refresh();
        }
    }

    private async void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel is null || e.NewValue is not FileNode { IsDirectory: false } node)
            return;

        await _viewModel.LoadPathAsync(node.FullPath);
        Refresh();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SaveCommand.CanExecute(null) == true)
            _viewModel.SaveCommand.Execute(null);
    }

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SaveAllCommand.CanExecute(null) == true)
            _viewModel.SaveAllCommand.Execute(null);
    }
}
