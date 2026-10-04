using System.Text.Json;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Tests;

public sealed class EditorRegressionTests
{
    [Fact]
    public void StructuralQa_DetectsBrokenTagAndPlaceholder()
    {
        const string original = "<RTP_Default>Урон {0}%</>";
        const string translated = "<RTP_Defaut>Урон %</>";

        var result = StructuralQaService.Analyze(original, translated);

        Assert.True(result.HasIssues);
        Assert.Contains("<RTP_Default>", result.MissingTags);
        Assert.Contains("<RTP_Defaut>", result.ExtraTags);
        Assert.Contains("{0}", result.MissingPlaceholders);
    }

    [Fact]
    public void StructureRestore_FixesSingleDamagedTag()
    {
        const string original = "<RTP_Default>造成{0}%伤害</>";
        const string translated = "<RTP_Defaut>Наносит {0}% урона</>";

        var restored = StructureProtectionService.RestoreStructure(original, translated);

        Assert.True(restored.Changed);
        Assert.Equal("<RTP_Default>Наносит {0}% урона</>", restored.Text);
        Assert.False(StructuralQaService.Analyze(original, restored.Text).HasIssues);
    }

    [Fact]
    public void TokenHighlight_SeparatesTechnicalTokens()
    {
        var segments = TokenHighlightService.Parse(
            "<RTP_Default>Текст {0} ${value} %s\\n</>");

        Assert.Contains(segments, x => x.Kind == HighlightTokenKind.Tag && x.Text == "<RTP_Default>");
        Assert.Contains(segments, x => x.Kind == HighlightTokenKind.Placeholder && x.Text == "{0}");
        Assert.Contains(segments, x => x.Kind == HighlightTokenKind.Placeholder && x.Text == "${value}");
        Assert.Contains(segments, x => x.Kind == HighlightTokenKind.Placeholder && x.Text == "%s");
        Assert.Contains(segments, x => x.Kind == HighlightTokenKind.Escape && x.Text == "\\n");
    }

    [Fact]
    public void RuleBasedCorrection_DoesNotLoseRequiredStructure()
    {
        var entry = MakeEntry(
            1,
            "Test",
            "skill",
            "<RTP_Default>Урон {0}%</>",
            "<RTP_Default>Урон  {0} % </>");

        var result = TranslationAutoCorrectionService.RuleBased.Correct(entry);

        Assert.True(result.HasChanges);
        Assert.False(StructuralQaService.Analyze(entry.Original, result.CorrectedText).HasIssues);
        Assert.Contains("{0}", result.CorrectedText);
        Assert.Contains("<RTP_Default>", result.CorrectedText);
        Assert.Contains("</>", result.CorrectedText);
    }

    [Fact]
    public void Consistency_FindsOneOriginalWithDifferentTranslations()
    {
        var document = new LocalizationDocument { FilePath = "memory.ndjson" };
        document.Entries.Add(MakeEntry(1, "N", "a", "确定", "Подтвердить"));
        document.Entries.Add(MakeEntry(2, "N", "b", "确定", "ОК"));
        document.Entries.Add(MakeEntry(3, "N", "c", "取消", "Отмена"));

        var issues = ConsistencyService.Analyze(document);

        var issue = Assert.Single(issues);
        Assert.Equal("确定", issue.Original);
        Assert.Equal(2, issue.VariantCount);
    }

    [Fact]
    public async Task NdjsonSave_WritesValidCompleteFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wojd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.ndjson");

        try
        {
            await File.WriteAllLinesAsync(
                path,
                [
                    "{\"namespace\":\"N\",\"key\":\"a\",\"original\":\"甲\",\"translation\":\"A\"}",
                    "{\"namespace\":\"N\",\"key\":\"b\",\"original\":\"乙\",\"translation\":\"B\"}"
                ]);

            var adapter = new NdjsonLocalizationAdapter();
            var document = await adapter.LoadAsync(path);
            document.Entries[0].Translation = "Новый перевод";

            await adapter.SaveAsync(document);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(2, lines.Length);

            foreach (var line in lines)
            {
                using var json = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            }

            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal(
                "Новый перевод",
                first.RootElement.GetProperty("translation").GetString());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Test cleanup should not hide assertion failures.
            }
        }
    }

    [Fact]
    public void MainWindowXaml_HasStableStaticEditorShell()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("x:Name=\"EditorRow\"", xaml);
        Assert.Contains("x:Name=\"EditorSplitter\"", xaml);
        Assert.Contains("x:Name=\"StructureQaBorder\"", xaml);
        Assert.Contains("x:Name=\"AutoCorrectButton\"", xaml);
        Assert.Contains("x:Name=\"BulkAutoCorrectButton\"", xaml);
        Assert.Contains("x:Name=\"AiReviewButton\"", xaml);
        Assert.Contains("x:Name=\"ReviewStatusCombo\"", xaml);
        Assert.Contains("<WrapPanel Grid.Row=\"0\"", xaml);
        Assert.DoesNotContain("Height=\"280\"", xaml);
    }

    private static LocalizationEntry MakeEntry(
        int index,
        string nameSpace,
        string key,
        string original,
        string translation)
    {
        var raw = JsonSerializer.Serialize(new
        {
            @namespace = nameSpace,
            key,
            original,
            translation
        });

        var entry = new LocalizationEntry
        {
            Index = index,
            Namespace = nameSpace,
            Key = key,
            Original = original,
            TranslationField = "translation",
            RawLine = raw
        };
        entry.InitializeSavedTranslation(translation);
        return entry;
    }
}
