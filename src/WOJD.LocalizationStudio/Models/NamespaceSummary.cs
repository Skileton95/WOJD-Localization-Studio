using WOJD.LocalizationStudio.Infrastructure;
namespace WOJD.LocalizationStudio.Models;
public sealed record NamespaceSummary(string Name, int Total, int Translated, int Untranslated, int Errors, string FilePath = "", bool Favorite = false)
{
    public string DisplayName => Name.Length == 0 ? "(без Namespace)" : Name;
    public bool IsExpanded { get; set; }
    public string Star => Favorite ? "★" : "☆";
    public string Counts => $"{Total:N0} / {Translated:N0} / {Untranslated:N0} / {Errors:N0} · {(Total == 0 ? 0 : 100.0 * Translated / Total):F1}%";
}
public sealed record NamespaceBookmark(string FilePath, string Namespace);
public sealed class NamespaceFileGroup : ObservableObject
{
    private bool _expanded = true;
    public required string FilePath { get; init; }
    public required string Label { get; init; }
    public required List<NamespaceSummary> Children { get; init; }
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
}
