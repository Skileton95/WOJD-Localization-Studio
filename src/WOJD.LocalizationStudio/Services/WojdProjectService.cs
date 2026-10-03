using System.IO;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class ProjectSource
{
    public string Path { get; set; } = "";
    public string Role { get; set; } = "RU";
    public string Format { get; set; } = "NDJSON";
}
public sealed class WojdProject
{
    public int Schema { get; set; } = 1;
    public string Name { get; set; } = "WOJD";
    public string GameVersion { get; set; } = "";
    public List<ProjectSource> Sources { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string ManifestPath { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Root => System.IO.Path.GetDirectoryName(ManifestPath)!;
}
public sealed record ProjectText(string FilePath, string Role, int Index, string Namespace, string Key, string Source, string Text);
public sealed record UnifiedProjectRow(string Namespace, string Key, string Chinese, string English, string Russian, string Files, bool Collision);
public sealed record ProjectReadResult(List<ProjectText> Texts, List<UnifiedProjectRow> Rows, List<string> Issues);
public static class WojdProjectService
{
    public static string Resolve(WojdProject project, string relative)
    {
        if (System.IO.Path.IsPathRooted(relative)) throw new IOException("Путь источника должен быть относительно корня проекта.");
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.Root, relative));
        if (!path.StartsWith(System.IO.Path.GetFullPath(project.Root).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Источник за пределами проекта.");
        return path;
    }
    public static void Validate(WojdProject project)
    {
        if (project.Schema != 1 || string.IsNullOrWhiteSpace(project.Name) || project.Sources is null) throw new IOException("Неподдерживаемый манифест проекта.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in project.Sources)
            if (source.Role is not ("CN" or "EN" or "RU") || source.Format is not ("NDJSON" or "fmtstring NDJSON" or "locres") || !paths.Add(Resolve(project, source.Path)))
                throw new IOException("Роль/формат источника неверны или путь повторяется.");
    }
    public static WojdProject Load(string path)
    {
        var project = JsonSerializer.Deserialize<WojdProject>(File.ReadAllText(path)) ?? throw new IOException("Пустой проект.");
        project.ManifestPath = System.IO.Path.GetFullPath(path); Validate(project); return project;
    }
    public static void Save(WojdProject project)
    {
        Validate(project); Directory.CreateDirectory(project.Root);
        if (File.Exists(project.ManifestPath)) File.Copy(project.ManifestPath, project.ManifestPath + "." + Guid.NewGuid().ToString("N") + ".bak");
        File.WriteAllText(project.ManifestPath + ".tmp", JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(project.ManifestPath + ".tmp", project.ManifestPath, true);
    }
    public static async Task<ProjectReadResult> ReadAsync(WojdProject project)
    {
        Validate(project); var texts = new List<ProjectText>(); var issues = new List<string>();
        foreach (var source in project.Sources)
        {
            var path = Resolve(project, source.Path);
            if (!File.Exists(path)) { issues.Add("Нет файла: " + source.Path); continue; }
            if (source.Format == "locres" || !new NdjsonLocalizationAdapter().CanOpen(path)) { issues.Add("Нужен проверенный экспорт NDJSON: " + source.Path); continue; }
            var document = await new NdjsonLocalizationAdapter().LoadAsync(path);
            issues.AddRange(document.LoadIssues.Select(x => $"{source.Path}:{x.Line}: {x.Message}"));
            texts.AddRange(document.Entries.Select(e => new ProjectText(path, source.Role, e.Index, e.Namespace, e.Key, e.Original,
                source.Role == "RU" ? e.Translation : string.IsNullOrEmpty(e.Translation) ? e.Original : e.Translation)));
        }
        var rows = texts.GroupBy(t => (t.Namespace, t.Key)).Select(g => new UnifiedProjectRow(g.Key.Namespace, g.Key.Key,
            string.Join(" | ", g.Where(t => t.Role == "CN").Select(t => t.Text)),
            string.Join(" | ", g.Where(t => t.Role == "EN").Select(t => t.Text)),
            string.Join(" | ", g.Where(t => t.Role == "RU").Select(t => t.Text)),
            string.Join("\n", g.Select(t => t.Role + ": " + t.FilePath + ":" + t.Index)),
            g.GroupBy(t => t.Role).Any(role => role.Count() > 1))).ToList();
        return new(texts, rows, issues);
    }
}
