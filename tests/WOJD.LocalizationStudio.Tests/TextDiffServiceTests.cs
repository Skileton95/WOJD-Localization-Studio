using WOJD.LocalizationStudio.Services;
using Xunit;

namespace WOJD.LocalizationStudio.Tests;

public sealed class TextDiffServiceTests
{
    [Fact]
    public void Compare_HighlightsMultipleChangedWords()
    {
        var diff = TextDiffService.Compare(
            "Наносит 10 урона каждые 2 сек.",
            "Наносит 15 урона каждые 3 сек.");

        Assert.Contains(diff.Left, x => x.Changed && x.Text.Contains("10", StringComparison.Ordinal));
        Assert.Contains(diff.Left, x => x.Changed && x.Text.Contains("2", StringComparison.Ordinal));
        Assert.Contains(diff.Right, x => x.Changed && x.Text.Contains("15", StringComparison.Ordinal));
        Assert.Contains(diff.Right, x => x.Changed && x.Text.Contains("3", StringComparison.Ordinal));
        Assert.Contains(diff.Left, x => !x.Changed && x.Text.Contains("Наносит", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CjkKeepsUnchangedCharactersSeparateFromChange()
    {
        var diff = TextDiffService.Compare("造成伤害", "造成大量伤害");

        Assert.Contains(diff.Right, x => x.Changed && x.Text.Contains("大量", StringComparison.Ordinal));
        Assert.Contains(diff.Right, x => !x.Changed && x.Text.Contains("造成", StringComparison.Ordinal));
        Assert.Contains(diff.Right, x => !x.Changed && x.Text.Contains("伤害", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_EqualStringsHaveNoChangedSegments()
    {
        var diff = TextDiffService.Compare("SkillName", "SkillName");

        Assert.Equal(0, diff.ChangedSegments);
        Assert.Single(diff.Left);
        Assert.False(diff.Left[0].Changed);
    }

    [Fact]
    public void Compare_EmptySideMarksOtherSideAsChanged()
    {
        var diff = TextDiffService.Compare(string.Empty, "Перевод");

        Assert.Empty(diff.Left);
        Assert.Contains(diff.Right, x => x.Changed && x.Text == "Перевод");
    }
}
