using System.Text.RegularExpressions;
namespace WOJD.LocalizationStudio.Models;
public sealed record SmartFilter(bool Untranslated = false, bool Qa = false, bool EmptySource = false, bool Chinese = false,
    bool Latin = false, bool Placeholders = false, bool LongTranslation = false)
{
    public bool Matches(LocalizationEntry row) =>
        (!Untranslated || string.IsNullOrWhiteSpace(row.Translation)) && (!Qa || row.HasValidationIssues) &&
        (!EmptySource || row.Original.Length == 0) &&
        (!Chinese || Regex.IsMatch(row.Original + row.Translation, @"[\u3400-\u9fff]")) &&
        (!Latin || Regex.IsMatch(row.Original + row.Translation, "[A-Za-z]")) &&
        (!Placeholders || Regex.IsMatch(row.Original, @"\{[^}]+\}|%[a-zA-Z]")) &&
        (!LongTranslation || row.Translation.Length > Math.Max(100, row.Original.Length * 2.5));
    public IReadOnlyList<FilterChip> Chips => new[]
    {
        (Untranslated, "Untranslated", "Без перевода"), (Qa, "Qa", "QA"), (EmptySource, "EmptySource", "Пустой оригинал"),
        (Chinese, "Chinese", "Китайский"), (Latin, "Latin", "Латиница"), (Placeholders, "Placeholders", "Плейсхолдеры"),
        (LongTranslation, "LongTranslation", "Длинный перевод")
    }.Where(x => x.Item1).Select(x => new FilterChip(x.Item2, x.Item3)).ToList();
    public SmartFilter Remove(string id) => id switch
    {
        "Untranslated" => this with { Untranslated = false }, "Qa" => this with { Qa = false },
        "EmptySource" => this with { EmptySource = false }, "Chinese" => this with { Chinese = false },
        "Latin" => this with { Latin = false }, "Placeholders" => this with { Placeholders = false },
        "LongTranslation" => this with { LongTranslation = false }, _ => this
    };
}
public sealed record FilterChip(string Id, string Label);
public sealed record SavedFilter(string Name, SmartFilter Criteria);
