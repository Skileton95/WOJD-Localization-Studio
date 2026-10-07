using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.ViewModels;

namespace WOJD.LocalizationStudio.Views;

public partial class StatisticsPage : UserControl
{
    private MainViewModel? _viewModel;

    public StatisticsPage()
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
        var entries = document?.Entries ?? [];
        var total = entries.Count;
        var translated = entries.Count(x => !string.IsNullOrWhiteSpace(x.Translation));
        var untranslated = total - translated;
        var errors = entries.Count(x => x.HasValidationIssues);
        var modified = entries.Count(x => x.Status == TranslationStatus.Modified);
        var structural = entries.Count(x => x.HasStructuralValidationIssues);
        var glossary = entries.Count(x => x.HasGlossaryIssue);
        var percent = total == 0 ? 0d : translated * 100d / total;

        TotalText.Text = $"{total:N0}";
        TranslatedText.Text = $"{translated:N0}";
        UntranslatedText.Text = $"{untranslated:N0}";
        ErrorText.Text = $"{errors:N0}";
        ModifiedText.Text = $"{modified:N0}";
        StructuralText.Text = $"{structural:N0}";
        GlossaryText.Text = $"{glossary:N0}";
        CompletionProgress.Value = percent;
        PercentText.Text = $"{percent:0.0}%";
        FileNameText.Text = document is null
            ? "Файл не открыт"
            : Path.GetFileName(document.FilePath);

        NamespaceGrid.ItemsSource = entries
            .GroupBy(x => x.Namespace ?? string.Empty, StringComparer.Ordinal)
            .Select(group =>
            {
                var list = group.ToList();
                var groupTranslated = list.Count(x => !string.IsNullOrWhiteSpace(x.Translation));
                return new NamespaceStat(
                    string.IsNullOrWhiteSpace(group.Key) ? "— без Namespace —" : group.Key,
                    list.Count,
                    groupTranslated,
                    list.Count(x => x.HasValidationIssues),
                    list.Count == 0 ? 0d : groupTranslated * 100d / list.Count);
            })
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TotalCount)
            or nameof(MainViewModel.TranslatedCount)
            or nameof(MainViewModel.UntranslatedCount)
            or nameof(MainViewModel.ModifiedCount)
            or nameof(MainViewModel.ErrorCount))
        {
            Refresh();
        }
    }

    private sealed record NamespaceStat(
        string Name,
        int Total,
        int Translated,
        int Errors,
        double Percent);
}
