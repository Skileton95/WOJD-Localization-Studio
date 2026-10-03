using WOJD.LocalizationStudio.Infrastructure;

namespace WOJD.LocalizationStudio.Models;

public sealed class SkillCardEntryView
{
    public required LocalizationEntry Entry { get; init; }
    public required string Role { get; init; }
    public bool IsShared { get; init; }
    public bool IsCustom { get; init; }

    public string LinkTypeText => IsShared ? "Общий" : "Уникальный";
    public string LinkTypeShort => IsShared ? "Общий" : "Уникальный";
    public string QaText => Entry.HasValidationIssues ? "Проверить" : "OK";
    public string Preview
        => !string.IsNullOrWhiteSpace(Entry.Translation)
            ? Entry.Translation
            : Entry.Original;
}

public sealed class SkillCardModel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<SkillCardEntryView> Entries { get; init; }

    public int UniqueCount => Entries.Count(x => !x.IsShared);
    public int SharedCount => Entries.Count(x => x.IsShared);
    public int LinkedCount => Entries.Count;
    public string LinkedLabel => $"{LinkedCount} связанных ключей";

    public SkillCardEntryView? NameEntry
        => Entries.FirstOrDefault(x => x.Role == "Название навыка")
           ?? Entries.FirstOrDefault();

    public SkillCardEntryView? DescriptionEntry
        => Entries.FirstOrDefault(x => x.Role == "Полное описание")
           ?? Entries.FirstOrDefault(x => x.Role.Contains("описание", StringComparison.OrdinalIgnoreCase));

    public string DescriptionPreview
        => DescriptionEntry?.Preview ?? "Описание не найдено";
}

public sealed class SkillCardLinkState
{
    public string SourcePath { get; set; } = string.Empty;
    public Dictionary<string, List<SkillCardLinkRef>> Links { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SkillCardLinkRef
{
    public string Namespace { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
