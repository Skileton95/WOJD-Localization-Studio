using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed class WorkAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Namespace { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public string Status { get; set; } = "Запланировано";
    public string Note { get; set; } = "";
}
public sealed class CollaborationState
{
    public int Schema { get; set; } = 1;
    public string Author { get; set; } = Environment.UserName;
    public List<WorkAssignment> Assignments { get; set; } = [];
}
public sealed record TaskBundleRow(string FilePath, int Index, string Namespace, string Key, string Source, string Before, string Proposal);
public sealed record TaskBundle(int Schema, string ProjectName, string GameVersion, WorkAssignment Assignment, string ExportedBy, DateTime AtUtc, List<TaskBundleRow> Rows);
public sealed class TaskImportRow
{
    public bool Include { get; set; }
    public bool ManualConfirmed { get; set; }
    public required string Kind { get; init; }
    public required TaskBundleRow Incoming { get; init; }
    public LocalizationEntry? Target { get; init; }
    public string FilePath => Incoming.FilePath;
    public string Namespace => Incoming.Namespace;
    public string Key => Incoming.Key;
    public string Source => Incoming.Source;
    public string Before { get; init; } = "";
    public string Proposal => Incoming.Proposal;
}
public static class CollaborationService
{
    public static void Validate(CollaborationState state)
    {
        if (state.Schema != 1 || string.IsNullOrWhiteSpace(state.Author) || state.Assignments is null || state.Assignments.Select(a => a.Id).Distinct().Count() != state.Assignments.Count || state.Assignments.Any(a => a.Id.Length == 0 || a.Status is not ("Запланировано" or "В работе" or "На проверке" or "Завершено")))
            throw new IOException("Проверьте автора, Id назначений и статусы: Запланировано / В работе / На проверке / Завершено.");
    }
    public static TaskBundle Export(string root, string name, string version, WorkAssignment assignment, string author, IEnumerable<LocalizationDocument> documents) =>
        new(1, name, version, assignment, author, DateTime.UtcNow, documents.SelectMany(d => d.Entries.Where(e => assignment.Namespace.Length == 0 || e.Namespace == assignment.Namespace)
            .Select(e => new TaskBundleRow(GitService.Relative(root, d.FilePath), e.Index, e.Namespace, e.Key, e.Original, e.SavedTranslation, e.Translation))).ToList());
    public static List<TaskImportRow> Preview(string root, IEnumerable<LocalizationDocument> documents, TaskBundle bundle)
    {
        if (bundle.Schema != 1 || bundle.Rows is null || bundle.Assignment is null) throw new IOException("Неподдерживаемый файл задания.");
        var targets = documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).ToList();
        var duplicate = bundle.Rows.GroupBy(r => (r.FilePath.ToUpperInvariant(), r.Namespace, r.Key)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var result = new List<TaskImportRow>();
        foreach (var row in bundle.Rows)
        {
            var full = Path.GetFullPath(Path.Combine(root, GitService.Relative(root, row.FilePath)));
            var matches = targets.Where(t => t.FilePath.Equals(full, StringComparison.OrdinalIgnoreCase) && t.Entry.Namespace == row.Namespace && t.Entry.Key == row.Key).ToList();
            var target = matches.Count == 1 ? matches[0].Entry : null;
            var kind = duplicate.Contains((row.FilePath.ToUpperInvariant(), row.Namespace, row.Key)) || matches.Count > 1 ? "Коллизия" :
                target is null ? "Нет строки" : target.Original != row.Source ? "Source изменён" : target.Translation == row.Proposal ? "Совпадает" : target.Translation != row.Before ? "Конфликт базы" : "Можно применить";
            result.Add(new() { Kind = kind, Incoming = row, Target = target, Before = target?.Translation ?? "", Include = kind == "Можно применить" });
        }
        return result;
    }
    public static List<(LocalizationEntry Entry, EntryField Field, string Before, string After)> Changes(IEnumerable<TaskImportRow> rows)
    {
        var selected = rows.Where(r => r.Include && (r.Kind == "Можно применить" || r.Kind == "Конфликт базы" && r.ManualConfirmed)).ToList();
        if (selected.Any(r => r.Target is null || r.Target.Namespace != r.Namespace || r.Target.Key != r.Key || r.Target.Original != r.Source || r.Target.Translation != r.Before)) throw new IOException("Предложения устарели или source изменён.");
        return selected.Select(r => (r.Target!, EntryField.Translation, r.Before, r.Proposal)).ToList();
    }
}
