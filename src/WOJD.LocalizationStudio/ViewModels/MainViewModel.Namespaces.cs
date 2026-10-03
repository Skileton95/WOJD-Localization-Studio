using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Infrastructure;

namespace WOJD.LocalizationStudio.ViewModels;

public sealed partial class MainViewModel
{
    private string _namespaceSearch = "";
    public BulkObservableCollection<NamespaceSummary> Namespaces { get; } = new();
    public string NamespaceSearch
    {
        get => _namespaceSearch;
        set { if (SetProperty(ref _namespaceSearch, value)) RefreshNamespaces(); }
    }
    public void SelectNamespace(string name) { StatusFilter = "Все"; NamespaceFilter = name; }
    public IReadOnlyList<LocalizationDocument> OpenDocuments => _sessions.Values.Select(x => x.Document).ToList();
    public void RefreshNamespaces()
    {
        Namespaces.ReplaceAll(Entries.GroupBy(x => x.Namespace, StringComparer.Ordinal)
            .Where(g => g.Key.Contains(NamespaceSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new NamespaceSummary(g.Key, g.Count(), g.Count(x => !string.IsNullOrWhiteSpace(x.Translation)),
                g.Count(x => string.IsNullOrWhiteSpace(x.Translation)), g.Count(x => x.HasValidationIssues))));
    }
}
