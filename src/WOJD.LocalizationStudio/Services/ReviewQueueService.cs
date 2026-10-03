using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record ReviewBaselineRow(string FilePath, int Index, string Namespace, string Key, string SourceHash);
public sealed record ReviewDecision(string FilePath, int Index, string Namespace, string Key, string SourceHash, string TranslationHash, string Status, string Comment, string Author, DateTime AtUtc);
public sealed record ReviewQueueRow(EntryLocation Location, string Reasons, bool HasErrors, string Status, string Comment)
{
    public string FilePath => Location.FilePath;
    public int Index => Location.Entry.Index;
    public string Namespace => Location.Entry.Namespace;
    public string Key => Location.Entry.Key;
    public string Source => Location.Entry.Original;
    public string Translation => Location.Entry.Translation;
}
public static class ReviewQueueService
{
    public static List<ReviewBaselineRow> Baseline(IEnumerable<LocalizationDocument> documents) => documents.SelectMany(d => d.Entries.Select(e => new ReviewBaselineRow(d.FilePath, e.Index, e.Namespace, e.Key, CollisionService.SourceHash(e.Original)))).ToList();
    public static List<ReviewQueueRow> Build(IEnumerable<LocalizationDocument> documents, IEnumerable<GlossaryTerm> glossary, IEnumerable<TermQaException> exceptions, WojdQaProfile profile,
        IEnumerable<ReviewDecision> decisions, IEnumerable<ReviewBaselineRow> baseline, IEnumerable<string>? intentionalConsistency = null)
    {
        var docs = documents.ToList(); var saved = decisions.GroupBy(d => (d.FilePath.ToUpperInvariant(), d.Index, d.Namespace, d.Key)).ToDictionary(g => g.Key, g => g.Last());
        var reference = baseline.GroupBy(d => (d.FilePath.ToUpperInvariant(), d.Index, d.Namespace, d.Key)).ToDictionary(g => g.Key, g => g.Last());
        var structural = WojdQaService.Analyze(docs, profile).GroupBy(i => i.Location.Entry).ToDictionary(g => g.Key, g => g.ToList());
        var terms = TerminologyQaService.Analyze(docs, glossary, exceptions).GroupBy(i => i.Location.Entry).ToDictionary(g => g.Key, g => g.ToList());
        var ignored = intentionalConsistency?.ToHashSet() ?? [];
        var variants = docs.SelectMany(d => d.Entries).Where(e => e.Original.Length > 0 && e.Translation.Length > 0).GroupBy(e => e.Original)
            .Where(g => !ignored.Contains(g.Key) && g.Select(e => e.Translation).Distinct().Take(2).Count() > 1).Select(g => g.Key).ToHashSet();
        var collisions = docs.SelectMany(d => d.Entries).GroupBy(e => (e.Namespace, e.Key)).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        var result = new List<ReviewQueueRow>();
        foreach (var doc in docs)
            foreach (var entry in doc.Entries)
            {
                var id = (doc.FilePath.ToUpperInvariant(), entry.Index, entry.Namespace, entry.Key);
                saved.TryGetValue(id, out var decision); reference.TryGetValue(id, out var prior);
                var reasons = new List<string>(); var hash = CollisionService.SourceHash(entry.Original);
                var valid = decision is not null && decision.SourceHash == hash && decision.TranslationHash == CollisionService.SourceHash(entry.Translation);
                if (prior is not null && prior.SourceHash != hash || decision is not null && decision.SourceHash != hash) reasons.Add("Source изменён");
                else if (reference.Count > 0 && prior is null) reasons.Add("Новая строка");
                if (structural.TryGetValue(entry, out var qa)) reasons.AddRange(qa.Select(i => i.Code));
                if (terms.TryGetValue(entry, out var term)) reasons.Add("Терминология");
                if (variants.Contains(entry.Original)) reasons.Add("Несколько вариантов");
                if (collisions.Contains(entry)) reasons.Add("Коллизия");
                var error = qa?.Any(i => i.Severity == "Ошибка") == true || term?.Any(i => i.Code != "Предпочтительный термин") == true ||
                    !string.IsNullOrWhiteSpace(entry.Original) && string.IsNullOrWhiteSpace(entry.Translation);
                var status = valid && !(decision!.Status == "Проверено" && error) ? decision!.Status : "Ожидает";
                if (status == "Ожидает" && reasons.Count == 0) reasons.Add("Не проверена");
                result.Add(new(new(doc.FilePath, entry), string.Join(" · ", reasons.Distinct()), error, status, valid ? decision!.Comment : ""));
            }
        return result;
    }
    public static ReviewDecision Decide(ReviewQueueRow row, string status, string comment, string author)
    {
        if (status is not ("Проверено" or "Отложено" or "Ожидает")) throw new ArgumentException("Неизвестный статус.");
        if (status == "Проверено" && row.HasErrors) throw new InvalidOperationException("Сначала исправьте ошибки/пустой перевод или оформите разрешённое исключение терминологии.");
        if (status == "Отложено" && string.IsNullOrWhiteSpace(comment)) throw new ArgumentException("Укажите причину откладывания.");
        return new(row.FilePath, row.Index, row.Namespace, row.Key, CollisionService.SourceHash(row.Source), CollisionService.SourceHash(row.Translation), status, comment, author, DateTime.UtcNow);
    }
}
