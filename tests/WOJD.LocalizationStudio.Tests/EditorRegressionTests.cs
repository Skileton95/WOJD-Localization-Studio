using System.Text.Json;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Tests;

public sealed class EditorRegressionTests
{
    [Fact]
    public void StructuralDiff_DetectsMissingTagAndPlaceholder()
    {
        var diff = StructuralDiffService.Analyze(
            "<RTP_Default>造成{0}%伤害</>",
            "Наносит урон");

        Assert.True(diff.HasIssues);
        Assert.Contains("<RTP_Default>", diff.MissingTokens);
        Assert.Contains("{0}", diff.MissingTokens);
        Assert.Contains("</>", diff.MissingTokens);
    }

    [Fact]
    public void StructureProtection_RestoresSingleMistypedTag()
    {
        var result = StructureProtectionService.RestoreStructure(
            "<RTP_Default>造成伤害</>",
            "<RTP_Defaut>Наносит урон</>");

        Assert.True(result.Changed);
        Assert.Contains("<RTP_Default>", result.Text);
        Assert.DoesNotContain("<RTP_Defaut>", result.Text);
    }

    [Fact]
    public void AutoCorrection_PreservesOriginalStructure()
    {
        var entry = CreateEntry(
            "<RTP_Default>造成{0}%伤害</>",
            "<RTP_Default>Наносит  {0} % урона</>");

        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);
        var structure = StructuralDiffService.Analyze(entry.Original, result.CorrectedText);

        Assert.False(structure.HasIssues);
        Assert.Contains("{0}", result.CorrectedText);
        Assert.Contains("<RTP_Default>", result.CorrectedText);
    }

    [Fact]
    public void TranslationMemory_ReturnsExactSourceSuggestion()
    {
        var source = CreateEntry("完成任务", "Завершить задание", 1);
        var query = CreateEntry("完成任务", string.Empty, 2);
        var index = TranslationMemoryIndex.Build([source, query]);

        var suggestion = Assert.Single(index.Search(query));
        Assert.Equal(100, suggestion.Score);
        Assert.Equal("Завершить задание", suggestion.Translation);
    }

    [Fact]
    public void Consistency_FindsDifferentTranslationsForSameSource()
    {
        var document = new LocalizationDocument { FilePath = "test.ndjson" };
        document.Entries.Add(CreateEntry("确定", "Подтвердить", 1));
        document.Entries.Add(CreateEntry("确定", "ОК", 2));
        document.Entries.Add(CreateEntry("取消", "Отмена", 3));

        var issues = ConsistencyService.Analyze(document);

        var issue = Assert.Single(issues);
        Assert.Equal("确定", issue.Original);
        Assert.Equal(2, issue.Variants.Count);
    }

    [Fact]
    public async Task NdjsonSave_RoundTripsAndLeavesValidJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wojd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "game.ndjson");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "{\"namespace\":\"Test\",\"key\":\"K1\",\"original\":\"确定\",\"translation\":\"ОК\"}\n");

            var adapter = new NdjsonLocalizationAdapter();
            var document = await adapter.LoadAsync(path);
            var entry = Assert.Single(document.Entries);
            entry.Translation = "Подтвердить";

            await adapter.SaveAsync(document);

            var savedLine = Assert.Single(await File.ReadAllLinesAsync(path));
            using var json = JsonDocument.Parse(savedLine);
            Assert.Equal("Подтвердить", json.RootElement.GetProperty("translation").GetString());
            Assert.False(Directory.EnumerateFiles(directory, "*.save.tmp").Any());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static LocalizationEntry CreateEntry(
        string original,
        string translation,
        int index = 1)
    {
        var entry = new LocalizationEntry
        {
            Index = index,
            Namespace = "Test",
            Key = "K" + index,
            Original = original,
            TranslationField = "translation",
            RawLine = "{\"namespace\":\"Test\",\"key\":\"K" + index + "\",\"original\":\"" + original.Replace("\"", "\\\"") + "\",\"translation\":\"\"}"
        };
        entry.InitializeSavedTranslation(translation);
        return entry;
    }
}
