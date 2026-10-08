using System.Runtime.ExceptionServices;
using System.Threading;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.Views;
using Xunit;

namespace WOJD.LocalizationStudio.Tests;

public sealed class ShellSmokeTests
{
    [Fact]
    public void ShellWindow_ConstructsWithoutRuntimeBindingErrors()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new ShellWindow();
                window.Close();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "ShellWindow smoke test timed out.");

        if (captured is not null)
            ExceptionDispatchInfo.Capture(captured).Throw();
    }

    [Fact]
    public async Task Ndjson_EndToEnd_LoadEditSaveReload_PreservesTranslation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wojd-shell-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "game.ndjson");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "{\"namespace\":\"UI\",\"key\":\"UI.Common.Confirm\",\"original\":\"确定\",\"translation\":\"ОК\"}\n");

            var adapter = new NdjsonLocalizationAdapter();
            var document = await adapter.LoadAsync(path);
            var entry = Assert.Single(document.Entries);
            entry.Translation = "Подтвердить";
            await adapter.SaveAsync(document);

            var reloaded = await adapter.LoadAsync(path);
            Assert.Equal("Подтвердить", Assert.Single(reloaded.Entries).Translation);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
