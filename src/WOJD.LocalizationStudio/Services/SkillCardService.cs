using System.Text.RegularExpressions;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class SkillCardService
{
    private static readonly Regex SkillIdRegex =
        new(
            @"(?i)skill[^0-9]{0,40}(?<id>\d{3,})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SharedRoleTokens =
    [
        "cost",
        "range",
        "radius",
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
                .OrderBy(x => ParseId(x.Key))
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
                if (views.Any(x =>
                        SameEntry(x.Entry, shared)))
                {
                    continue;
                }

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
                    x.Entry.Key.Contains(
                        "name",
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

        var match = SkillIdRegex.Match(key);

        return match.Success
            ? match.Groups["id"].Value
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

        if (normalized.Contains("shortdesc"))
            return "Краткое описание";

        if (normalized.Contains("extradesc"))
            return "Доп. информация";

        if (normalized.Contains("notequipped"))
            return "Предупреждение";

        if (normalized.Contains("shortinfo"))
            return "Краткая информация";

        if (normalized.Contains("description") ||
            normalized.EndsWith("desc", StringComparison.Ordinal))
        {
            return "Полное описание";
        }

        if (normalized.Contains("cooldown") ||
            normalized.EndsWith("cd", StringComparison.Ordinal))
        {
            return isShared
                ? "Перезарядка"
                : "Значение перезарядки";
        }

        if (normalized.Contains("casttime"))
        {
            return isShared
                ? "Применение"
                : "Значение применения";
        }

        if (normalized.Contains("radius"))
        {
            return isShared
                ? "Радиус"
                : "Значение радиуса";
        }

        if (normalized.Contains("range"))
        {
            return isShared
                ? "Дистанция"
                : "Значение дистанции";
        }

        if (normalized.Contains("cost"))
        {
            return isShared
                ? "Расход"
                : "Значение расхода";
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
            "Расход" => 30,
            "Значение расхода" => 31,
            "Дистанция" => 40,
            "Значение дистанции" => 41,
            "Радиус" => 50,
            "Значение радиуса" => 51,
            "Применение" => 60,
            "Значение применения" => 61,
            "Перезарядка" => 70,
            "Значение перезарядки" => 71,
            "Полное описание" => 80,
            "Доп. информация" => 90,
            "Предупреждение" => 100,
            "Краткая информация" => 110,
            "Эффект" => 120,
            "Подсказка" => 130,
            "Уровень навыка" => 140,
            _ => 500
        };

    private static long ParseId(string id)
        => long.TryParse(id, out var value)
            ? value
            : long.MaxValue;
}
