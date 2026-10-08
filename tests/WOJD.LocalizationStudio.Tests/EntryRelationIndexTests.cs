using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;

namespace WOJD.LocalizationStudio.Tests;

public sealed class EntryRelationIndexTests
{
    [Fact]
    public void KeyFamily_GroupsSkillFieldsAndSeries()
    {
        Assert.Equal("450_0", EntryRelationIndex.GetKeyFamily("450_0-SkillName"));
        Assert.Equal("450_0", EntryRelationIndex.GetKeyFamily("450_0-SkillDesc"));
        Assert.Equal("450", EntryRelationIndex.GetSeriesFamily("450_0"));
        Assert.Equal("UI.Common", EntryRelationIndex.GetKeyFamily("UI.Common.Confirm"));
    }

    [Fact]
    public void RelatedRows_IncludeFamilySeriesAndSameOriginal()
    {
        var document = new LocalizationDocument { FilePath = "test.ndjson" };
        var name = Entry(1, "Skill", "450_0-SkillName", "斩击", "Удар");
        var description = Entry(2, "Skill", "450_0-SkillDesc", "造成伤害", "Наносит урон");
        var variant = Entry(3, "Skill", "450_1-SkillName", "重斩", "Тяжёлый удар");
        var sameOriginal = Entry(4, "Other", "Menu-Confirm", "斩击", "Удар");
        var unrelated = Entry(5, "Other", "Window-Cancel", "取消", "Отмена");
        document.Entries.AddRange([name, description, variant, sameOriginal, unrelated]);

        var index = EntryRelationIndex.For(document);
        var related = index.GetRelated(name, 20);

        Assert.Contains(description, related);
        Assert.Contains(variant, related);
        Assert.Contains(sameOriginal, related);
        Assert.DoesNotContain(unrelated, related);
        Assert.Equal("группа 450_0", index.DescribeRelation(name, description));
        Assert.Equal("тот же Original", index.DescribeRelation(name, sameOriginal));
    }

    [Fact]
    public void EntryList_IndexOfTracksAppendInsertAndRemove()
    {
        var entries = new LocalizationEntryList();
        var first = Entry(1, "", "A", "甲", "A");
        var second = Entry(2, "", "B", "乙", "B");
        var inserted = Entry(3, "", "C", "丙", "C");

        entries.Add(first);
        entries.Add(second);
        Assert.Equal(0, entries.IndexOf(first));
        Assert.Equal(1, entries.IndexOf(second));

        entries.Insert(1, inserted);
        Assert.Equal(1, entries.IndexOf(inserted));
        Assert.Equal(2, entries.IndexOf(second));

        entries.Remove(first);
        Assert.Equal(0, entries.IndexOf(inserted));
        Assert.Equal(1, entries.IndexOf(second));
    }

    [Fact]
    public void VisualGroup_StartsAgainWhenFamilyChanges()
    {
        var document = new LocalizationDocument();
        var a1 = Entry(1, "", "450_0-SkillName", "A", "A");
        var a2 = Entry(2, "", "450_0-SkillDesc", "B", "B");
        var b1 = Entry(3, "", "900_0-SkillName", "C", "C");
        var a3 = Entry(4, "", "450_0-SkillCost", "D", "D");
        document.Entries.AddRange([a1, a2, b1, a3]);

        var index = EntryRelationIndex.For(document);

        Assert.True(index.StartsVisualGroup(a1));
        Assert.False(index.StartsVisualGroup(a2));
        Assert.True(index.StartsVisualGroup(b1));
        Assert.True(index.StartsVisualGroup(a3));
        Assert.Equal(3, index.GetPosition(a3));
    }

    private static LocalizationEntry Entry(
        int index,
        string ns,
        string key,
        string original,
        string translation)
    {
        var entry = new LocalizationEntry
        {
            Index = index,
            Namespace = ns,
            Key = key,
            Original = original,
            TranslationField = "translation",
            RawLine = "{}"
        };
        entry.InitializeSavedTranslation(translation);
        return entry;
    }
}
