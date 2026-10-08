using System.Diagnostics;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using Xunit;

namespace WOJD.LocalizationStudio.Tests;

public sealed class LargeFilePerformanceTests
{
    [Fact]
    public void RefreshValidation_SecondPassAcross600kRows_StaysFast()
    {
        const int rowCount = 600_000;
        var entries = new LocalizationEntry[rowCount];

        for (var i = 0; i < rowCount; i++)
        {
            var entry = new LocalizationEntry
            {
                Index = i + 1,
                Namespace = "Perf",
                Key = "Perf.Row",
                Original = "测试文本",
                TranslationField = "translation"
            };
            entry.InitializeSavedTranslation(string.Empty);
            entries[i] = entry;
        }

        var stopwatch = Stopwatch.StartNew();
        foreach (var entry in entries)
            entry.RefreshValidation();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Memoized validation of {rowCount:N0} unchanged rows took {stopwatch.Elapsed}. " +
            "This hot path must not re-run full QA for every row.");
    }

    [Fact]
    public void RelatedLookup_CommonOriginalDoesNotSortEntireBucket()
    {
        const int duplicateCount = 100_000;
        var document = new LocalizationDocument();
        LocalizationEntry? selected = null;

        for (var i = 0; i < duplicateCount; i++)
        {
            var entry = new LocalizationEntry
            {
                Index = i + 1,
                Namespace = "Perf",
                Key = "Common-Key",
                Original = "确定",
                TranslationField = "translation"
            };
            entry.InitializeSavedTranslation("Подтвердить");
            document.Entries.Add(entry);
            selected ??= entry;
        }

        var index = EntryRelationIndex.For(document);
        var stopwatch = Stopwatch.StartNew();
        var related = index.GetRelated(selected!, 24);
        stopwatch.Stop();

        Assert.Equal(24, related.Count);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Related lookup over a {duplicateCount:N0}-row duplicate bucket took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void InitializeSavedTranslation_DoesNotRaiseUiNotifications()
    {
        var entry = new LocalizationEntry
        {
            Index = 1,
            Namespace = "Perf",
            Key = "Perf.Row",
            Original = "测试文本"
        };
        var notifications = 0;
        entry.PropertyChanged += (_, _) => notifications++;

        entry.InitializeSavedTranslation("Перевод");

        Assert.Equal(0, notifications);
        Assert.Equal("Перевод", entry.Translation);
    }

    [Fact]
    public void ValidationCache_RevalidatesAfterQaConfigurationChanges()
    {
        var entry = new LocalizationEntry
        {
            Index = 1,
            Namespace = "Perf",
            Key = "Perf.Row",
            Original = "测试文本"
        };
        entry.InitializeSavedTranslation("Перевод");

        var notifications = 0;
        entry.PropertyChanged += (_, _) => notifications++;
        entry.RefreshValidation();
        Assert.Equal(0, notifications);

        TranslationValidator.NotifyConfigurationChanged();
        entry.RefreshValidation();
        Assert.True(notifications > 0);
    }

    [Fact]
    public void Validator_FastPathsPreserveStructuralAndSameSourceQa()
    {
        var structural = TranslationValidator.Validate(
            "造成{0}%伤害<RTP_Default>",
            "Наносит урон");
        Assert.Contains(TranslationIssueKind.Placeholder, structural.Kinds);
        Assert.Contains(TranslationIssueKind.Tag, structural.Kinds);

        var sameSource = TranslationValidator.Validate("确定", "确定");
        Assert.Contains(TranslationIssueKind.SameAsSource, sameSource.Kinds);
    }

    [Fact]
    public void EmptyValidation_ReusesImmutableResultState()
    {
        var first = TranslationValidator.Validate("测试", string.Empty);
        var second = TranslationValidator.Validate("другая строка", string.Empty);

        Assert.Equal(0, first.IssueCount);
        Assert.Equal(0, second.IssueCount);
        Assert.Same(first.Kinds, second.Kinds);
    }
}
