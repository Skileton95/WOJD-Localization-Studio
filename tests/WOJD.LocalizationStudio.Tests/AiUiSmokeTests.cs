using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Views;
using Xunit;

namespace WOJD.LocalizationStudio.Tests;

public sealed class AiUiSmokeTests
{
    [Fact]
    public void QaPage_Loaded_InstallsManualAutoAndAiActions()
        => RunSta(() =>
        {
            var page = new QaPage();
            var window = new Window { Content = page, Width = 1200, Height = 700 };
            window.Show();
            page.UpdateLayout();

            var categoryList = Assert.IsType<ListBox>(page.FindName("CategoryList"));
            var leftGrid = Assert.IsType<Grid>(categoryList.Parent);
            var labels = Descendants<Button>(leftGrid)
                .Select(x => x.Content?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            Assert.Contains("Исправить вручную", labels);
            Assert.Contains(labels, x => x!.StartsWith("Автоисправить", StringComparison.Ordinal));
            Assert.Contains("✦ ИИ-исправление", labels);
            Assert.Contains("✦ ИИ для категории", labels);
            window.Close();
        });

    [Fact]
    public void TranslationPage_Loaded_InstallsAiCorrectionButton()
        => RunSta(() =>
        {
            var page = new TranslationPage();
            var window = new Window { Content = page, Width = 1400, Height = 800 };
            window.Show();
            page.UpdateLayout();

            var auto = Assert.IsType<Button>(page.FindName("AutoCorrectButton"));
            var panel = Assert.IsAssignableFrom<Panel>(auto.Parent);
            Assert.Contains(panel.Children.OfType<Button>(), x => Equals(x.Content, "✦ ИИ-исправление"));
            window.Close();
        });

    [Fact]
    public void SettingsPage_Loaded_InstallsAiCategory()
        => RunSta(() =>
        {
            var page = new SettingsPage();
            var window = new Window { Content = page, Width = 1000, Height = 700 };
            window.Show();
            page.UpdateLayout();

            var root = Assert.IsType<Grid>(page.Content);
            var sidebar = root.Children.OfType<Border>().First(x => Grid.GetColumn(x) == 0).Child;
            Assert.Contains(Descendants<Button>((DependencyObject)sidebar), x => Equals(x.Content, "✦  ИИ"));
            window.Close();
        });

    [Fact]
    public void AiBatchEstimate_UsesConfiguredPricesWithoutNetworkCalls()
    {
        var entry = new WOJD.LocalizationStudio.Models.LocalizationEntry
        {
            Index = 1,
            Namespace = "UI",
            Key = "UI.Common.Confirm",
            Original = "确定"
        };
        entry.InitializeSavedTranslation("Подтвердить");

        var settings = new EditorSettings
        {
            AiInputUsdPerMillionTokens = 1m,
            AiOutputUsdPerMillionTokens = 2m
        };
        var estimate = AiBatchReviewService.Estimate([entry], settings);

        Assert.Equal(1, estimate.Entries);
        Assert.True(estimate.ApproxInputTokens > 0);
        Assert.True(estimate.ApproxOutputTokens > 0);
        Assert.NotNull(estimate.EstimatedUsd);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "AI UI smoke test timed out.");
        if (captured is not null)
            ExceptionDispatchInfo.Capture(captured).Throw();
    }
}
