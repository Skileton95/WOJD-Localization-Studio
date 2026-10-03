using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Infrastructure;
namespace WOJD.LocalizationStudio.Services;

public sealed class ReplaceCandidate : ObservableObject
{
    private bool _include = true;
    public bool Include { get => _include; set => SetProperty(ref _include, value); }
    public required string FilePath { get; init; }
    public required LocalizationEntry Entry { get; init; }
    public required EntryField Field { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
}
public static class ProjectReplaceService
{
    public static List<ReplaceCandidate> Preview(IEnumerable<LocalizationDocument> documents, EntryField field,
        string query, string replacement, bool regex, bool matchCase, bool wholeWord, CancellationToken cancellation = default)
    {
        if (query.Length == 0) throw new ArgumentException("Введите текст поиска.");
        var pattern = regex ? query : Regex.Escape(query);
        if (wholeWord) pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
        var matcher = new Regex(pattern, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
            TimeSpan.FromMilliseconds(250));
        var rows = new List<ReplaceCandidate>();
        foreach (var doc in documents)
            foreach (var entry in doc.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var before = entry.GetField(field);
                var after = regex ? matcher.Replace(before, replacement) : matcher.Replace(before, _ => replacement);
                if (before != after) rows.Add(new() { FilePath = doc.FilePath, Entry = entry, Field = field, Before = before, After = after });
            }
        return rows;
    }
}