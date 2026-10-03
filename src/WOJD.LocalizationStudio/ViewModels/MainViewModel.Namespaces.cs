using System.IO;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Infrastructure;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    private readonly Dictionary<LocalizationDocument, List<NamespaceSummary>> _namespaceCache = [];
    private string _namespaceSearch = "";
    private bool _namespacePanelExpanded = true;
    private HashSet<(string FilePath, string Namespace)> _namespaceFavorites = [];
    public BulkObservableCollection<NamespaceSummary> Namespaces { get; } = new();
    public BulkObservableCollection<NamespaceFileGroup> NamespaceTree { get; } = new();
    public bool NamespacePanelExpanded { get => _namespacePanelExpanded; set { if (SetProperty(ref _namespacePanelExpanded, value)) ScheduleWorkspaceSave(); } }
    public string NamespaceSearch { get => _namespaceSearch; set { if (SetProperty(ref _namespaceSearch, value)) RefreshNamespaces(); } }
    public void SelectNamespace(string name) { StatusFilter = "Все"; NamespaceFilter = name; }
    public IReadOnlyList<LocalizationDocument> OpenDocuments => _sessions.Values.Select(x => x.Document).ToList();
    public void ToggleNamespaceFavorite(NamespaceSummary row)
    {
        var key = (row.FilePath, row.Name); if (!_namespaceFavorites.Add(key)) _namespaceFavorites.Remove(key);
        RefreshNamespaces(); ScheduleWorkspaceSave();
    }
    public void MoveNamespace(int direction)
    {
        if (Namespaces.Count == 0) return;
        var index = Namespaces.ToList().FindIndex(x => x.Name == NamespaceFilter);
        if (index < 0) index = direction > 0 ? -1 : 0;
        SelectNamespace(Namespaces[(index + direction + Namespaces.Count) % Namespaces.Count].Name);
        SelectedEntry = EntriesView.Cast<LocalizationEntry>().FirstOrDefault();
    }
    public void RefreshNamespaces()
    {
        var documents = OpenDocuments.ToHashSet();
        foreach (var old in _namespaceCache.Keys.Where(x => !documents.Contains(x)).ToArray()) _namespaceCache.Remove(old);
        List<NamespaceSummary> Summaries(LocalizationDocument d)
        {
            if (!_namespaceCache.TryGetValue(d, out var cached))
            {
                cached = d.Entries.GroupBy(x => x.Namespace, StringComparer.Ordinal)
                    .Select(g => new NamespaceSummary(g.Key, g.Count(), g.Count(x => !string.IsNullOrWhiteSpace(x.Translation)),
                        g.Count(x => string.IsNullOrWhiteSpace(x.Translation)), g.Count(x => x.HasValidationIssues), d.FilePath)).ToList();
                _namespaceCache[d] = cached;
            }
            return cached.Where(x => x.Name.Contains(NamespaceSearch, StringComparison.OrdinalIgnoreCase))
                .Select(x => x with { Favorite = _namespaceFavorites.Contains((d.FilePath, x.Name)) })
                .OrderByDescending(x => x.Favorite).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        }
        var expanded = NamespaceTree.ToDictionary(x => x.FilePath, x => x.IsExpanded, StringComparer.OrdinalIgnoreCase);
        NamespaceTree.ReplaceAll(OpenDocuments.Select(d => new NamespaceFileGroup { FilePath = d.FilePath,
            Label = $"{Path.GetFileName(d.FilePath)} · {d.Entries.Count:N0}", Children = Summaries(d), IsExpanded = !expanded.TryGetValue(d.FilePath, out var state) || state })
            .Where(x => x.Children.Count > 0));
        Namespaces.ReplaceAll(ActiveDocument is { } active ? Summaries(active) : []);
    }
}