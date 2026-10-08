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
        _viewModel = viewModel;
        Refresh();
    }

    public void Detach()
    {
        _viewModel = null;
    }

    public void Refresh()
    {
        var document = _viewModel?.ActiveDocument;
        var entries = document?.Entries;
        var total = entries?.Count ?? 0;

        var translated = 0;
        var errors = 0;
        var modified = 0;
        var structural = 0;
        var tags = 0;
        var placeholders = 0;
        var newLines = 0;
        var glossary = 0;
        var missingSource = 0;
        var sameSource = 0;

        var namespaceStats = new Dictionary<string, NamespaceAccumulator>(StringComparer.Ordinal);
        var firstTranslationByOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
        var inconsistentOriginals = new HashSet<string>(StringComparer.Ordinal);

        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                var hasTranslation = !string.IsNullOrWhiteSpace(entry.Translation);
                if (hasTranslation)
                    translated++;
                if (entry.HasValidationIssues)
                    errors++;
                if (entry.Status == TranslationStatus.Modified)
                    modified++;
                if (entry.HasStructuralValidationIssues)
                    structural++;
                if (entry.HasTagIssues)
                    tags++;
                if (entry.HasPlaceholderIssues)
                    placeholders++;
                if (entry.HasNewLineIssues)
                    newLines++;
                if (entry.HasGlossaryIssue)
                    glossary++;
                if (entry.HasSourceMissingIssue || string.IsNullOrWhiteSpace(entry.Original))
                    missingSource++;
                if (entry.HasSameAsSourceIssue)
                    sameSource++;

                var ns = entry.Namespace ?? string.Empty;
                if (!namespaceStats.TryGetValue(ns, out var stat))
                {
                    stat = new NamespaceAccumulator(ns);
                    namespaceStats[ns] = stat;
                }

                stat.Total++;
                if (hasTranslation)
                    stat.Translated++;
                else
                    stat.Untranslated++;
                if (entry.HasValidationIssues)
                    stat.Errors++;

                if (!string.IsNullOrWhiteSpace(entry.Original) && hasTranslation)
                {
                    if (!firstTranslationByOriginal.TryGetValue(entry.Original, out var first))
                    {
                        firstTranslationByOriginal[entry.Original] = entry.Translation;
                    }
                    else if (!string.Equals(first, entry.Translation, StringComparison.Ordinal))
                    {
                        inconsistentOriginals.Add(entry.Original);
                    }
                }
            }
        }

        var consistencyRows = 0;
        if (entries is not null && inconsistentOriginals.Count > 0)
        {
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Original)
                    && inconsistentOriginals.Contains(entry.Original))
                {
                    consistencyRows++;
                }
            }
        }

        var untranslated = total - translated;
        var percent = total == 0 ? 0d : translated * 100d / total;

        TotalText.Text = $"{total:N0}";
        TranslatedText.Text = $"{translated:N0}";
        UntranslatedText.Text = $"{untranslated:N0}";
        ErrorText.Text = $"{errors:N0}";
        ModifiedText.Text = $"{modified:N0}";
        StructuralText.Text = $"{structural:N0}";
        TagsText.Text = $"{tags:N0}";
        PlaceholdersText.Text = $"{placeholders:N0}";
        NewLinesText.Text = $"{newLines:N0}";
        GlossaryText.Text = $"{glossary:N0}";
        MissingSourceText.Text = $"{missingSource:N0}";
        ConsistencyText.Text = $"{consistencyRows:N0}";
        SameSourceText.Text = $"{sameSource:N0}";
        CompletionProgress.Value = percent;
        PercentText.Text = $"{percent:0.0}%";
        FileNameText.Text = document is null
            ? "Файл не открыт"
            : Path.GetFileName(document.FilePath);

        NamespaceGrid.ItemsSource = namespaceStats.Values
            .Select(x => x.ToStat())
            .OrderByDescending(x => x.Untranslated)
            .ThenByDescending(x => x.Errors)
            .ThenByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class NamespaceAccumulator(string name)
    {
        public string Name { get; } = name;
        public int Total { get; set; }
        public int Translated { get; set; }
        public int Untranslated { get; set; }
        public int Errors { get; set; }

        public NamespaceStat ToStat()
            => new(
                string.IsNullOrWhiteSpace(Name) ? "— без Namespace —" : Name,
                Total,
                Translated,
                Untranslated,
                Errors,
                Total == 0 ? 0d : Translated * 100d / Total);
    }

    private sealed record NamespaceStat(
        string Name,
        int Total,
        int Translated,
        int Untranslated,
        int Errors,
        double Percent);
}
