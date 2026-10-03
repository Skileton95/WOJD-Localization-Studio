using System.IO;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public IReadOnlyList<string> ProjectFilePaths
    {
        get
        {
            static IEnumerable<FileNode> Walk(IEnumerable<FileNode> nodes) => nodes.SelectMany(n => n.IsDirectory ? Walk(n.Children) : new[] { n });
            return Walk(FileTree).Select(x => x.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
    public async Task<ProjectSearchResponse> SearchProjectStreamingAsync(string query, bool matchCase, bool exact, bool regex,
        CancellationToken cancellation, IProgress<FileOperationProgress>? progress)
    {
        RememberProjectSearchQuery(query);
        var files = ProjectFilePaths; var loaded = OpenDocuments.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        var results = new List<ProjectSearchResult>();
        for (var i = 0; i < files.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var file = files[i];
            progress?.Report(new(100.0 * i / Math.Max(1, files.Count), i, $"Поиск: {Path.GetFileName(file)} ({i + 1}/{files.Count})"));
            ProjectSearchResponse response;
            if (loaded.TryGetValue(file, out var document))
                response = await Task.Run(() => ProjectSearchService.SearchLocations(document.Entries.Select(e => new EntryLocation(file, e)), query, matchCase, exact, regex, cancellation), cancellation);
            else
            {
                var cache = await ProjectSearchIndexService.EnsureAsync(file, cancellation, progress);
                response = await Task.Run(() => ProjectSearchService.SearchLocations(ProjectSearchIndexService.Read(cache, file, cancellation), query, matchCase, exact, regex, cancellation), cancellation);
            }
            results.AddRange(response.Results.Take(50000 - results.Count));
            if (response.IsTruncated || results.Count >= 50000) return new(results, true);
        }
        return new(results, false);
    }
    public async Task OpenProjectSearchResultAsync(ProjectSearchResult result)
    {
        await LoadPathAsync(result.FilePath);
        if (!_sessions.TryGetValue(Path.GetFullPath(result.FilePath), out var session)) throw new IOException("Файл результата не удалось открыть.");
        var row = session.Document.Entries.FirstOrDefault(e => e.Index == result.Index && e.Namespace == result.Namespace && e.Key == result.Key && e.Original == result.Entry.Original)
            ?? throw new IOException("Результат устарел. Повторите поиск.");
        OpenProjectSearchResult(result with { Entry = row });
    }
}