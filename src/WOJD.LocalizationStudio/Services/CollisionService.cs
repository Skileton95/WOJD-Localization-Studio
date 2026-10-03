using System.Security.Cryptography;
using System.Text;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record CollisionChoice(string Namespace, string Key, string FilePath, int Index, string SourceHash, string Rule, DateTime AtUtc);
public sealed record CollisionCandidate(string Namespace, string Key, string FilePath, int Index, string Source, string Translation, string Decision, EntryLocation Location);
public static class CollisionService
{
    public static string SourceHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static List<CollisionCandidate> Analyze(IEnumerable<LocalizationDocument> documents, IEnumerable<CollisionChoice> choices)
    {
        var saved = choices.ToList();
        return documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).GroupBy(x => (x.Entry.Namespace, x.Entry.Key)).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(loc =>
            {
                var choice = saved.LastOrDefault(c => c.Namespace == g.Key.Namespace && c.Key == g.Key.Key);
                var match = choice is not null && choice.FilePath.Equals(loc.FilePath, StringComparison.OrdinalIgnoreCase) && choice.Index == loc.Entry.Index && choice.SourceHash == SourceHash(loc.Entry.Original);
                return new CollisionCandidate(g.Key.Namespace, g.Key.Key, loc.FilePath, loc.Entry.Index, loc.Entry.Original, loc.Entry.Translation,
                    match ? "Выбран: " + choice!.Rule : choice?.Rule == "Заблокировано" ? "Заблокировано" : "Не выбран", loc);
            })).ToList();
    }
    public static Dictionary<(string Namespace, string Key), EntryLocation> Resolve(IEnumerable<LocalizationDocument> documents, IEnumerable<CollisionChoice> choices)
    {
        var saved = choices.ToList(); var result = new Dictionary<(string, string), EntryLocation>();
        foreach (var group in documents.SelectMany(d => d.Entries.Select(e => new EntryLocation(d.FilePath, e))).GroupBy(l => (l.Entry.Namespace, l.Entry.Key)))
        {
            var choice = saved.LastOrDefault(c => c.Namespace == group.Key.Namespace && c.Key == group.Key.Key);
            if (choice?.Rule == "Заблокировано") continue;
            if (group.Count() == 1) { result[group.Key] = group.Single(); continue; }
            var matches = group.Where(l => choice is not null && l.FilePath.Equals(choice.FilePath, StringComparison.OrdinalIgnoreCase) && l.Entry.Index == choice.Index && SourceHash(l.Entry.Original) == choice.SourceHash).ToList();
            if (matches.Count == 1) result[group.Key] = matches[0];
        }
        return result;
    }
}
