using System.IO;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;

namespace WOJD.LocalizationStudio.Services;

public sealed class QaProfileRule
{
    public string Name { get; set; } = string.Empty;
    public string NamespacePattern { get; set; } = "*";
    public string KeyPattern { get; set; } = "*";
    public int? MaxCharacters { get; set; }
    public bool? AllowNewLines { get; set; }
}

public static class QaProfileService
{
    private static readonly object Sync = new();
    private static IReadOnlyList<QaProfileRule>? _rules;

    public static string SettingsPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WOJD.LocalizationStudio",
            "qa-profiles.json");

    public static IReadOnlyList<string> Validate(
        string nameSpace,
        string key,
        string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return [];

        var issues = new List<string>();

        foreach (var rule in GetRules())
        {
            if (!Matches(rule.NamespacePattern, nameSpace) ||
                !Matches(rule.KeyPattern, key))
            {
                continue;
            }

            if (rule.MaxCharacters is int max &&
                translation.Length > max)
            {
                issues.Add(
                    $"{rule.Name}: длина {translation.Length}, максимум {max}");
            }

            if (rule.AllowNewLines == false &&
                ContainsNewLine(translation))
            {
                issues.Add(
                    $"{rule.Name}: переносы строк запрещены");
            }
        }

        return issues;
    }

    public static IReadOnlyList<QaProfileRule> GetRules()
    {
        lock (Sync)
        {
            if (_rules is not null)
                return _rules;

            EnsureDefaultFile();

            try
            {
                _rules =
                    JsonSerializer.Deserialize<List<QaProfileRule>>(
                        File.ReadAllText(SettingsPath, Encoding.UTF8),
                        JsonOptions)
                    ?? DefaultRules();
            }
            catch
            {
                _rules = DefaultRules();
            }

            return _rules;
        }
    }

    public static void Reload()
    {
        lock (Sync)
            _rules = null;
    }

    public static void SaveRules(IEnumerable<QaProfileRule> rules)
    {
        var normalized = rules
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new QaProfileRule
            {
                Name = x.Name.Trim(),
                NamespacePattern = string.IsNullOrWhiteSpace(x.NamespacePattern)
                    ? "*"
                    : x.NamespacePattern.Trim(),
                KeyPattern = string.IsNullOrWhiteSpace(x.KeyPattern)
                    ? "*"
                    : x.KeyPattern.Trim(),
                MaxCharacters = x.MaxCharacters is > 0
                    ? x.MaxCharacters
                    : null,
                AllowNewLines = x.AllowNewLines
            })
            .ToList();

        Directory.CreateDirectory(
            Path.GetDirectoryName(SettingsPath)!);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(normalized, JsonOptions),
            new UTF8Encoding(false));

        lock (Sync)
            _rules = normalized;
    }

    private static void EnsureDefaultFile()
    {
        if (File.Exists(SettingsPath))
            return;

        Directory.CreateDirectory(
            Path.GetDirectoryName(SettingsPath)!);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(DefaultRules(), JsonOptions),
            new UTF8Encoding(false));
    }

    private static List<QaProfileRule> DefaultRules()
        =>
        [
            new QaProfileRule
            {
                Name = "Название навыка",
                NamespacePattern = "*",
                KeyPattern = "*-SkillName",
                MaxCharacters = 32,
                AllowNewLines = false
            }
        ];

    private static bool Matches(string pattern, string value)
        => FileSystemName.MatchesSimpleExpression(
            string.IsNullOrWhiteSpace(pattern) ? "*" : pattern,
            value ?? string.Empty,
            ignoreCase: true);

    private static bool ContainsNewLine(string value)
        => value.Contains('\n') ||
           value.Contains('\r') ||
           value.Contains("\\n", StringComparison.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
}
