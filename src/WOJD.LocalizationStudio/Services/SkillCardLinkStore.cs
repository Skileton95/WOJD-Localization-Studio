using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public static class SkillCardLinkStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public static SkillCardLinkState Load(string sourcePath)
    {
        var state =
            new SkillCardLinkState
            {
                SourcePath = sourcePath
            };

        var path = GetPath(sourcePath);

        if (!File.Exists(path))
            return state;

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<SkillCardLinkState>(json);

            if (loaded is null)
                return state;

            loaded.SourcePath = sourcePath;
            loaded.Links ??=
                new Dictionary<string, List<SkillCardLinkRef>>(
                    StringComparer.OrdinalIgnoreCase);

            if (loaded.Links.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                loaded.Links =
                    new Dictionary<string, List<SkillCardLinkRef>>(
                        loaded.Links,
                        StringComparer.OrdinalIgnoreCase);
            }

            return loaded;
        }
        catch
        {
            return state;
        }
    }

    public static void Save(
        string sourcePath,
        SkillCardLinkState state)
    {
        try
        {
            state.SourcePath = sourcePath;

            var path = GetPath(sourcePath);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(state, JsonOptions),
                new UTF8Encoding(false));
        }
        catch
        {
            // Карточки навыков являются вспомогательным представлением.
            // Ошибка сохранения связей не должна мешать работе редактора.
        }
    }

    public static bool AddLink(
        SkillCardLinkState state,
        string skillId,
        LocalizationEntry entry)
    {
        if (!state.Links.TryGetValue(
                skillId,
                out var links))
        {
            links = [];
            state.Links[skillId] = links;
        }

        if (links.Any(x =>
                string.Equals(
                    x.Namespace,
                    entry.Namespace,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.Key,
                    entry.Key,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        links.Add(
            new SkillCardLinkRef
            {
                Namespace = entry.Namespace,
                Key = entry.Key
            });

        return true;
    }

    public static bool RemoveLink(
        SkillCardLinkState state,
        string skillId,
        LocalizationEntry entry)
    {
        if (!state.Links.TryGetValue(
                skillId,
                out var links))
        {
            return false;
        }

        var removed =
            links.RemoveAll(x =>
                string.Equals(
                    x.Namespace,
                    entry.Namespace,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.Key,
                    entry.Key,
                    StringComparison.OrdinalIgnoreCase)) > 0;

        if (links.Count == 0)
            state.Links.Remove(skillId);

        return removed;
    }

    private static string GetPath(string sourcePath)
    {
        var normalized =
            Path.GetFullPath(sourcePath)
                .ToLowerInvariant();

        var hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(normalized)))
                .ToLowerInvariant();

        var root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WOJD.LocalizationStudio",
                "SkillCards");

        return Path.Combine(root, hash + ".json");
    }
}
