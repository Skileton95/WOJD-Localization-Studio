using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class SkillCardService
{
    private static readonly Regex WojdSkillKeyRegex =
        new(
            @"^(?<id>\d+_\d+)-Skill(?<field>[A-Za-z0-9_]+)$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

    // Оставляем поддержку старого эвристического формата для совместимости
    // с пользовательскими/тестовыми файлами, но WOJD-ключи проверяются первыми.
    private static readonly Regex LegacySkillIdRegex =
        new(
            @"skill[^0-9]{0,40}(?<id>\d{3,})",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

    private static readonly string[] SharedRoleTokens =
    [
        "cost",
        "range",
        "radius",
        "distance",
        "casttime",
        "cast_time",
        "cooldown",
        "cool_down",
        "shortinfo",
        "short_info",
        "level"
    ];

    public static IReadOnlyList<SkillCardModel> Build(
        LocalizationDocument document,
        SkillCardLinkState? linkState = null)
    {
        var entries = document.Entries;

        var uniqueGroups =
            entries
                .Select(entry =>
                    new
                    {
                        Entry = entry,
                        SkillId = TryExtractSkillId(entry.Key)
                    })
                .Where(x => !string.IsNullOrWhiteSpace(x.SkillId))
                .GroupBy(
                    x => x.SkillId!,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => ParseBaseId(x.Key))
                .ThenBy(x => ParseVariantId(x.Key))
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var sharedEntries =
            entries
                .Where(IsSharedSkillEntry)
                .GroupBy(
                    x => GetRole(x.Key, isShared: true),
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetRoleOrder(GetRole(x.Key, isShared: true)))
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var result = new List<SkillCardModel>(uniqueGroups.Count);

        foreach (var group in uniqueGroups)
        {
            var views =
                group
                    .Select(x =>
                        new SkillCardEntryView
                        {
                            Entry = x.Entry,
                            Role = GetRole(x.Entry.Key, isShared: false),
                            IsShared = false,
                            IsCustom = false
                        })
                    .OrderBy(x => GetRoleOrder(x.Role))
                    .ThenBy(x => x.Entry.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var shared in sharedEntries)
            {
                if (views.Any(x => SameEntry(x.Entry, shared)))
                    continue;

                views.Add(
                    new SkillCardEntryView
                    {
                        Entry = shared,
                        Role = GetRole(shared.Key, isShared: true),
                        IsShared = true,
                        IsCustom = false
                    });
            }

            if (linkState?.Links.TryGetValue(
                    group.Key,
                    out var customLinks) == true)
            {
                foreach (var link in customLinks)
                {
                    var custom =
                        entries.FirstOrDefault(x =>
                            string.Equals(
                                x.Namespace,
                                link.Namespace,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                x.Key,
                                link.Key,
                                StringComparison.OrdinalIgnoreCase));

                    if (custom is null ||
                        views.Any(x => SameEntry(x.Entry, custom)))
                    {
                        continue;
                    }

                    views.Add(
                        new SkillCardEntryView
                        {
                            Entry = custom,
                            Role = GetRole(custom.Key, isShared: true),
                            IsShared = true,
                            IsCustom = true
                        });
                }
            }

            views =
                views
                    .OrderBy(x => x.IsShared ? 1 : 0)
                    .ThenBy(x => GetRoleOrder(x.Role))
                    .ThenBy(x => x.Entry.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var nameEntry =
                views.FirstOrDefault(x => x.Role == "Название навыка")
                ?? views.FirstOrDefault(x =>
                    x.Entry.Key.EndsWith(
                        "SkillName",
                        StringComparison.OrdinalIgnoreCase));

            var displayName =
                nameEntry is null
                    ? $"Навык {group.Key}"
                    : !string.IsNullOrWhiteSpace(nameEntry.Entry.Translation)
                        ? nameEntry.Entry.Translation
                        : !string.IsNullOrWhiteSpace(nameEntry.Entry.Original)
                            ? nameEntry.Entry.Original
                            : $"Навык {group.Key}";

            result.Add(
                new SkillCardModel
                {
                    Id = group.Key,
                    DisplayName = displayName,
                    Entries = views
                });
        }

        return result;
    }

    public static string? TryExtractSkillId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var wojdMatch = WojdSkillKeyRegex.Match(key);

        if (wojdMatch.Success)
            return wojdMatch.Groups["id"].Value;

        var legacyMatch = LegacySkillIdRegex.Match(key);

        return legacyMatch.Success
            ? legacyMatch.Groups["id"].Value
            : null;
    }

    public static string GetRole(
        string key,
        bool isShared)
    {
        var normalized =
            key.Replace("_", string.Empty)
               .Replace("-", string.Empty)
               .ToLowerInvariant();

        // WOJD: NNNN_N-SkillComplexDesc — подробная карточка.
        if (normalized.Contains("skillcomplexdesc") ||
            normalized.Contains("complexdesc"))
        {
            return "Подробное описание";
        }

        // WOJD: NNNN_N-SkillDesc — краткая карточка.
        if (normalized.Contains("skilldesc") ||
            normalized.Contains("shortdesc"))
        {
            return "Краткое описание";
        }

        if (normalized.Contains("skillname"))
            return "Название навыка";

        if (normalized.Contains("skillcasttime") ||
            normalized.Contains("casttime"))
        {
            return "Время применения";
        }

        if (normalized.Contains("skillcooldown") ||
            normalized.Contains("cooldown") ||
            normalized.EndsWith("cd", StringComparison.Ordinal))
        {
            return "Перезарядка";
        }

        if (normalized.Contains("skillcost") ||
            normalized.Contains("cost"))
        {
            return "Стоимость";
        }

        if (normalized.Contains("skilldistance") ||
            normalized.Contains("distance"))
        {
            return "Дальность применения";
        }

        if (normalized.Contains("skillrange") ||
            normalized.Contains("range") ||
            normalized.Contains("radius"))
        {
            return "Радиус / зона действия";
        }

        if (normalized.Contains("skilltag") ||
            normalized.Contains("tag1") ||
            normalized.Contains("tag2"))
        {
            return "Тег";
        }

        if (normalized.Contains("extradesc"))
            return "Доп. информация";

        if (normalized.Contains("notequipped"))
            return "Предупреждение";

        if (normalized.Contains("shortinfo"))
            return "Краткая информация";

        if (normalized.Contains("description") ||
            normalized.EndsWith("desc", StringComparison.Ordinal))
        {
            return "Подробное описание";
        }

        if (normalized.Contains("level"))
            return "Уровень навыка";

        if (normalized.Contains("effect"))
            return "Эффект";

        if (normalized.Contains("tip"))
            return "Подсказка";

        if (normalized.Contains("name"))
            return "Название навыка";

        return "Связанная строка";
    }

    private static bool IsSharedSkillEntry(
        LocalizationEntry entry)
    {
        if (TryExtractSkillId(entry.Key) is not null)
            return false;

        if (!entry.Key.Contains(
                "skill",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized =
            entry.Key.Replace("_", string.Empty)
                     .Replace("-", string.Empty)
                     .ToLowerInvariant();

        return SharedRoleTokens.Any(token =>
            normalized.Contains(
                token.Replace("_", string.Empty),
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameEntry(
        LocalizationEntry left,
        LocalizationEntry right)
        => string.Equals(
               left.Namespace,
               right.Namespace,
               StringComparison.OrdinalIgnoreCase) &&
           string.Equals(
               left.Key,
               right.Key,
               StringComparison.OrdinalIgnoreCase);

    private static int GetRoleOrder(string role)
        => role switch
        {
            "Название навыка" => 10,
            "Краткое описание" => 20,
            "Подробное описание" => 30,
            "Время применения" => 40,
            "Перезарядка" => 50,
            "Стоимость" => 60,
            "Дальность применения" => 70,
            "Радиус / зона действия" => 80,
            "Тег" => 90,
            "Доп. информация" => 100,
            "Предупреждение" => 110,
            "Краткая информация" => 120,
            "Эффект" => 130,
            "Подсказка" => 140,
            "Уровень навыка" => 150,
            _ => 500
        };

    private static long ParseBaseId(string id)
    {
        var separator = id.IndexOf('_');
        var value = separator >= 0 ? id[..separator] : id;

        return long.TryParse(value, out var parsed)
            ? parsed
            : long.MaxValue;
    }

    private static long ParseVariantId(string id)
    {
        var separator = id.IndexOf('_');

        if (separator < 0 || separator + 1 >= id.Length)
            return 0;

        return long.TryParse(id[(separator + 1)..], out var parsed)
            ? parsed
            : long.MaxValue;
    }
}
