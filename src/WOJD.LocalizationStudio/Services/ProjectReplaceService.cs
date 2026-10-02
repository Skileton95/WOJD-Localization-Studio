using System.IO;
using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Infrastructure;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class ProjectReplaceCandidate : ObservableObject
{
    private bool _isSelected = true;

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Namespace { get; init; }
    public required string Key { get; init; }
    public required string Original { get; init; }
    public required string CurrentTranslation { get; init; }
    public required string NewTranslation { get; init; }
    public required LocalizationEntry Entry { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public static class ProjectReplaceService
{
    public static List<ProjectReplaceCandidate> Preview(
        IEnumerable<LocalizationDocument> documents,
        string query,
        string replacement,
        bool matchCase,
        bool wholeWord,
        bool useRegex)
    {
        var result =
            new List<ProjectReplaceCandidate>();

        if (string.IsNullOrEmpty(query))
            return result;

        Regex? regex = null;

        if (useRegex)
        {
            regex =
                new Regex(
                    query,
                    RegexOptions.CultureInvariant |
                    (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
                    TimeSpan.FromMilliseconds(750));
        }
        else if (wholeWord)
        {
            regex =
                new Regex(
                    $@"(?<![p{{L}}p{{N}}_]){Regex.Escape(query)}(?![p{{L}}p{{N}}_])",
                    RegexOptions.CultureInvariant |
                    (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
                    TimeSpan.FromMilliseconds(750));
        }

        var comparison =
            matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

        foreach (var document in documents)
        {
            foreach (var entry in document.Entries)
            {
                var current =
                    entry.Translation;

                string? updated = null;

                if (regex is not null)
                {
                    if (regex.IsMatch(current))
                    {
                        updated =
                            regex.Replace(
                                current,
                                replacement);
                    }
                }
                else if (current.Contains(
                             query,
                             comparison))
                {
                    updated =
                        current.Replace(
                            query,
                            replacement,
                            comparison);
                }

                if (updated is null ||
                    string.Equals(
                        current,
                        updated,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(
                    new ProjectReplaceCandidate
                    {
                        FilePath = document.FilePath,
                        FileName = Path.GetFileName(document.FilePath),
                        Namespace = entry.Namespace,
                        Key = entry.Key,
                        Original = entry.Original,
                        CurrentTranslation = current,
                        NewTranslation = updated,
                        Entry = entry
                    });
            }
        }

        return result;
    }
}
