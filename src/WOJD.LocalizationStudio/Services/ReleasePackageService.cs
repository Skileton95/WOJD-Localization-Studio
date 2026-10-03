using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record ReleaseSnapshotRow(string FilePath, int Index, string Namespace, string Key, string Source, string Translation);
public sealed record ReleaseBaseline(string Version, DateTime AtUtc, List<ReleaseSnapshotRow> Rows);
public sealed record ReleaseChange(string Kind, ReleaseSnapshotRow? Before, ReleaseSnapshotRow? After);
public sealed record PackagePreflight(List<string> Errors, List<string> Warnings);
public static class ReleasePackageService
{
    public static ReleaseBaseline Snapshot(string version, IEnumerable<LocalizationDocument> documents) =>
        new(version, DateTime.UtcNow, documents.SelectMany(d => d.Entries.Select(e => new ReleaseSnapshotRow(d.FilePath, e.Index, e.Namespace, e.Key, e.Original, e.Translation))).ToList());
    public static List<ReleaseChange> Changes(ReleaseBaseline current, ReleaseBaseline? previous)
    {
        var before = (previous?.Rows ?? []).ToDictionary(r => (r.FilePath.ToUpperInvariant(), r.Index, r.Namespace, r.Key));
        var after = current.Rows.ToDictionary(r => (r.FilePath.ToUpperInvariant(), r.Index, r.Namespace, r.Key));
        return before.Keys.Union(after.Keys).Select(id =>
        {
            before.TryGetValue(id, out var old); after.TryGetValue(id, out var fresh);
            return new ReleaseChange(old is null ? "Добавлена" : fresh is null ? "Удалена" : old.Source != fresh.Source ? "Source изменён" : old.Translation != fresh.Translation ? "Перевод изменён" : "Без изменений", old, fresh);
        }).Where(c => c.Kind != "Без изменений").ToList();
    }
    public static PackagePreflight Preflight(IEnumerable<LocalizationDocument> documents, WojdQaProfile profile, IEnumerable<GlossaryTerm> glossary, IEnumerable<TermQaException> exceptions, IEnumerable<CollisionChoice> choices)
    {
        var docs = documents.ToList(); var errors = new List<string>(); var warnings = new List<string>();
        foreach (var doc in docs)
        {
            try { FileSafetyService.CheckUnchanged(doc); } catch (Exception e) { errors.Add(e.Message); }
            errors.AddRange(doc.LoadIssues.Select(i => $"{doc.FilePath}:{i.Line}: {i.Message}"));
        }
        foreach (var issue in WojdQaService.Analyze(docs, profile)) (issue.Severity == "Ошибка" ? errors : warnings).Add($"{issue.FilePath}:{issue.Index}: {issue.Code}: {issue.Detail}");
        foreach (var issue in TerminologyQaService.Analyze(docs, glossary, exceptions)) (issue.Code == "Предпочтительный термин" ? warnings : errors).Add($"{issue.FilePath}:{issue.Index}: {issue.Detail}");
        var resolved = CollisionService.Resolve(docs, choices);
        foreach (var group in docs.SelectMany(d => d.Entries).GroupBy(e => (e.Namespace, e.Key)).Where(g => g.Count() > 1))
            if (!resolved.ContainsKey(group.Key)) errors.Add($"Нерешённая коллизия: {group.Key.Namespace}:{group.Key.Key}");
            else warnings.Add($"Выбран кандидат коллизии {group.Key.Namespace}:{group.Key.Key}; физические строки сохранены.");
        if (docs.Count == 0) errors.Add("Нет русских NDJSON-файлов для упаковки.");
        return new(errors, warnings);
    }
    public static async Task<ReleaseBaseline> CreateAsync(string destination, string version, string comment, string? root, IReadOnlyList<LocalizationDocument> documents, ReleaseBaseline? previous,
        WojdQaProfile profile, IEnumerable<GlossaryTerm> glossary, IEnumerable<TermQaException> exceptions, IEnumerable<CollisionChoice> choices, bool allowWarnings)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 80 || version.Any(c => !(char.IsLetterOrDigit(c) || ".-_".Contains(c)))) throw new IOException("Укажите версию без разделителей пути.");
        var check = Preflight(documents, profile, glossary, exceptions, choices);
        if (check.Errors.Count > 0 || check.Warnings.Count > 0 && !allowWarnings) throw new IOException($"Preflight: ошибок {check.Errors.Count}, предупреждений {check.Warnings.Count}. Проверьте отчёт.");
        if (File.Exists(destination) || documents.Any(d => Path.GetFullPath(d.FilePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))) throw new IOException("Выберите новое имя пакета.");
        var snapshot = Snapshot(version, documents); var changes = Changes(snapshot, previous);
        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".wojd-package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var changedPaths = changes.Where(c => c.After is not null).Select(c => c.After!.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var payloads = new List<(string Path, string ArchivePath, string Hash)>();
            foreach (var doc in documents.Where(d => changedPaths.Contains(d.FilePath)))
            {
                var file = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".ndjson");
                await DocumentSnapshotService.WriteAsync(doc, file);
                var relative = root is null ? CollisionService.SourceHash(doc.FilePath)[..8] + "/" + Path.GetFileName(doc.FilePath) : Path.GetRelativePath(root, doc.FilePath);
                if (Path.IsPathRooted(relative) || relative.StartsWith("..")) throw new IOException("Файл пакета вне корня проекта.");
                payloads.Add((file, "files/" + relative.Replace('\\', '/'), FileSafetyService.Hash(file)));
            }
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var item in payloads) archive.CreateEntryFromFile(item.Path, item.ArchivePath);
                async Task Write(string name, string text) { await using var stream = archive.CreateEntry(name).Open(); await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(text); }
                var changedRows = changes.Where(c => c.After is not null).Select(c => c.After!).Select(r =>
                {
                    var doc = documents.Single(d => d.FilePath == r.FilePath); var entry = doc.Entries.Single(e => e.Index == r.Index && e.Namespace == r.Namespace && e.Key == r.Key);
                    return NdjsonLocalizationAdapter.SerializeEntry(entry);
                });
                await Write("changed-rows.ndjson", string.Join("\n", changedRows) + (changes.Any(c => c.After is not null) ? "\n" : ""));
                await Write("changes.json", JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = true }));
                await Write("manifest.json", JsonSerializer.Serialize(new { Schema = 1, Version = version, PreviousVersion = previous?.Version, snapshot.AtUtc, Draft = allowWarnings && check.Warnings.Count > 0, Format = "NDJSON export; binary rebuild requires verified external tooling", Files = payloads.Select(p => new { Name = p.ArchivePath, Sha256 = p.Hash }), Preflight = check }, new JsonSerializerOptions { WriteIndented = true }));
                await Write("CHANGELOG.md", $"# Перевод {version}\n\nБаза: {previous?.Version ?? "нет"}\n\n{comment}\n\n" + string.Join("\n", changes.GroupBy(c => c.Kind).Select(g => $"- {g.Key}: {g.Count()}")) + "\n\nNDJSON-экспорт. Сборка locres/fmtstring и проверка в игре выполняются внешним инструментом.\n");
            }
            foreach (var doc in documents) FileSafetyService.CheckUnchanged(doc);
            File.Move(temporary, destination); return snapshot;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); Directory.Delete(staging, true); }
    }
}
