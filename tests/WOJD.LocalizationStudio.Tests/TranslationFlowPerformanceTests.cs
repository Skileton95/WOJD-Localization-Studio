using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using Xunit;

namespace WOJD.LocalizationStudio.Tests;

public sealed class TranslationFlowPerformanceTests
{
    [Fact]
    public void SameOriginalPropagation_ExaminesOnlyIndexedBucket()
    {
        var document = new LocalizationDocument();
        for (var i = 0; i < 100_000; i++)
        {
            var entry = Entry(i + 1, $"Key-{i}", string.Empty, "Перевод");
            document.Entries.Add(entry);
        }

        var source = Entry(100_001, "Confirm-A", "确定", "Подтвердить");
        var duplicateB = Entry(100_002, "Confirm-B", "确定", "ОК");
        var duplicateC = Entry(100_003, "Confirm-C", "确定", string.Empty);
        document.Entries.Add(source);
        document.Entries.Add(duplicateB);
        document.Entries.Add(duplicateC);

        var relations = EntryRelationIndex.For(document);
        var plan = TranslationPropagationService.BuildSameOriginalPlan(relations, source);

        Assert.Equal(3, plan.ExaminedRows);
        Assert.Equal(2, plan.Changes.Count);
        TranslationPropagationService.Apply(plan);
        Assert.Equal("Подтвердить", duplicateB.Translation);
        Assert.Equal("Подтвердить", duplicateC.Translation);
    }

    [Fact]
    public void QaIncrementalUpdate_DoesNotTraverseWholeDocument()
    {
        var document = new LocalizationDocument();
        for (var i = 0; i < 100_000; i++)
            document.Entries.Add(Entry(i + 1, $"Key-{i}", string.Empty, string.Empty));

        var source = Entry(100_001, "Skill-Name-A", "技能", "Навык");
        var duplicate = Entry(100_002, "Skill-Name-B", "技能", "Умение");
        document.Entries.Add(source);
        document.Entries.Add(duplicate);

        var relations = EntryRelationIndex.For(document);
        var qa = new QaIssueIndex(document.Entries);
        Assert.True(qa.Count("consistency") >= 2);

        duplicate.Translation = "Навык";
        qa.UpdateAfterTranslation(duplicate, relations);

        Assert.Equal(2, qa.LastIncrementalExaminedEntries);
        Assert.False(qa.Contains("consistency", source));
        Assert.False(qa.Contains("consistency", duplicate));
    }

    [Fact]
    public void FamilyScopedQa_UsesIndexedFamilyInsteadOfGlobalCategory()
    {
        var document = new LocalizationDocument();
        var name = Entry(1, "450_0-SkillName", "技能", string.Empty);
        var desc = Entry(2, "450_0-SkillDesc", "描述", string.Empty);
        var other = Entry(3, "900_0-SkillName", "其他", string.Empty);
        document.Entries.AddRange([name, desc, other]);

        var relations = EntryRelationIndex.For(document);
        var qa = new QaIssueIndex(document.Entries);
        var family = qa.GetFamilyCategory("untranslated", name, relations);

        Assert.Equal(2, family.Count);
        Assert.Contains(name, family);
        Assert.Contains(desc, family);
        Assert.DoesNotContain(other, family);
    }

    private static LocalizationEntry Entry(
        int index,
        string key,
        string original,
        string translation)
    {
        var entry = new LocalizationEntry
        {
            Index = index,
            Namespace = "Perf",
            Key = key,
            Original = original,
            TranslationField = "translation",
            RawLine = "{}"
        };
        entry.InitializeSavedTranslation(translation);
        return entry;
    }
}
