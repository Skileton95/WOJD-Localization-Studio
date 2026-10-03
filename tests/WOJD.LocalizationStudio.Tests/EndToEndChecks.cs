using System.IO;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using WOJD.LocalizationStudio.Models;
using WOJD.LocalizationStudio.Services;
using WOJD.LocalizationStudio.ViewModels;
internal static class EndToEndChecks
{
    public static async Task<MainViewModel> Run(string parent)
    {
        var root = Path.Combine(parent, "end-to-end"); Directory.CreateDirectory(root);
        var cn = Path.Combine(root, "cn.ndjson"); var en = Path.Combine(root, "en.ndjson"); var ru = Path.Combine(root, "ru.ndjson"); var old = Path.Combine(root, "old.ndjson");
        var sources = new[] { "开始 {Player}", "警告 <Em>危险</>" };
        var translations = new[] { "Начать {Player}", "Внимание <Em>опасно</>" };
        var keys = new[] { "menu_start", "fmt_warning" };
        string Rows(string[] texts, bool filled, bool alias) => string.Join("\r\n", Enumerable.Range(0, 2).Select(i => alias
            ? JsonSerializer.Serialize(new { Namespace = "UI", Key = keys[i], Source = sources[i], translated = filled ? texts[i] : "", opaque = new { value = 42 } })
            : JsonSerializer.Serialize(new { @namespace = "UI", key = keys[i], source = sources[i], translation = filled ? texts[i] : "", opaque = new { value = 42 } })));
        await File.WriteAllTextAsync(cn, Rows(sources, false, false));
        await File.WriteAllTextAsync(en, Rows(["Start {Player}", "Warning <Em>danger</>"], true, false));
        await File.WriteAllTextAsync(ru, Rows(translations, false, true), new UnicodeEncoding(false, true, true));
        await File.WriteAllTextAsync(old, Rows(translations, true, false));
        var cnHash = FileSafetyService.Hash(cn); var enHash = FileSafetyService.Hash(en);
        var project = new WojdProject { ManifestPath = Path.Combine(root, "e2e.wojd-project.json"), Name = "Fixture", GameVersion = "test", Sources = [new() { Path = "cn.ndjson", Role = "CN" }, new() { Path = "en.ndjson", Role = "EN" }, new() { Path = "ru.ndjson", Role = "RU", Format = "fmtstring NDJSON" }] };
        WojdProjectService.Save(project);
        var vm = new MainViewModel(); await vm.OpenWojdProjectAsync(project.ManifestPath);
        Program.Check(vm.OpenTabs.Count == 1 && vm.ActiveDocument!.ProviderId == "fmtstring-ndjson", "E2E opens only RU with declared provider");
        var targets = await vm.WojdTargetsAsync(); var previous = await new NdjsonLocalizationAdapter().LoadAsync(old);
        var preview = ProjectImportService.Preview(targets, [previous], "RU", false);
        vm.ApplyBatch(ProjectImportService.Changes(preview), "E2E import"); vm.UndoCommand.Execute(null);
        Program.Check(targets.Single().Entries.All(e => e.Translation == ""), "E2E import atomic Undo");
        vm.RedoCommand.Execute(null); Program.Check(targets.Single().Entries[1].Translation == translations[1], "E2E import Redo");
        var glossary = new List<GlossaryTerm> { new() { Chinese = "开始", Russian = "Начать" }, new() { Chinese = "危险", Russian = "опасно" } };
        GlossaryService.Save(Path.Combine(vm.ProjectDataDirectory, "glossary.json"), glossary); vm.ReloadGlossary();
        Program.Check(WojdQaService.Analyze(targets, new()).Count == 0 && TerminologyQaService.Analyze(targets, vm.Glossary, []).Count == 0, "E2E format and glossary QA");
        var queue = ReviewQueueService.Build(targets, glossary, [], new(), [], []);
        var decisions = queue.Select(r => ReviewQueueService.Decide(r, "Проверено", "", "fixture")).ToList(); ProjectMetadataService.Save(Path.Combine(vm.ProjectDataDirectory, "reviews.json"), decisions);
        Program.Check(ReviewQueueService.Build(targets, glossary, [], new(), decisions, []).All(r => r.Status == "Проверено"), "E2E review persistence");
        vm.SetAnnotation(targets.Single().Entries[0], "Контекст fixture", "Термин");
        var provider = new FormatProviderRegistry(); provider.DeclareProject(project); await provider.SaveAsync(targets.Single());
        await vm.ReloadActiveFromDiskAsync(); targets = await vm.WojdTargetsAsync();
        var saved = targets.Single();
        var bytes = await File.ReadAllBytesAsync(ru); using var savedJson = JsonDocument.Parse(File.ReadAllLines(ru)[0]);
        Program.Check(bytes[0] == 255 && bytes[1] == 254 && !saved.HasFinalNewLine && savedJson.RootElement.GetProperty("opaque").GetProperty("value").GetInt32() == 42 && saved.Entries[0].Key == keys[0], "E2E UTF16, aliases, unknown fields and keys preserved");
        await SqliteProjectIndexService.EnsureAsync(ru, default, null);
        Program.Check(SqliteProjectIndexService.Read(ru, default).Count() == 2, "E2E SQLite projection");
        var search = await vm.SearchProjectStreamingAsync("Start {Player}", false, true, false, default, null);
        Program.Check(search.Results.Count == 1 && vm.OpenTabs.Count == 1, "E2E search CN/EN without opening source");
        await vm.OpenProjectSearchResultAsync(search.Results[0]);
        Program.Check(vm.ActiveDocument!.IsReadOnly, "E2E source opens read-only");
        vm.SelectedEntry!.Translation = "FORBIDDEN";
        Program.Check(vm.SelectedEntry.Translation != "FORBIDDEN", "E2E source edits rejected");
        var directSaveBlocked = false; try { await new NdjsonLocalizationAdapter().SaveAsync(vm.ActiveDocument!); } catch (IOException) { directSaveBlocked = true; }
        Program.Check(directSaveBlocked, "E2E direct adapter save protects source");
        await vm.ReloadActiveFromDiskAsync();
        Program.Check(vm.ActiveDocument!.IsReadOnly, "E2E source reload remains read-only");
        Program.Check(ProjectStatisticsService.Calculate(vm.OpenDocuments).Rows == 2 && ConsistencyService.Analyze(vm.OpenDocuments).Conflicts.Count == 0, "E2E CN/EN excluded from translation statistics/consistency");
        Program.Check(ProjectReplaceService.Preview(vm.OpenDocuments, EntryField.Translation, "Start", "Begin", false, true, false).Count == 0, "E2E replace skips source documents");
        Program.Check(FileComparisonService.Compare(vm.ActiveDocument, previous).Items.All(i => !i.CanTransfer), "E2E legacy comparison protects source");
        await vm.LoadPathAsync(ru);
        var package = Path.Combine(root, "fixture-release.zip");
        var baseline = await ReleasePackageService.CreateAsync(package, "test-1", "End-to-end fixture", root, await vm.WojdTargetsAsync(), null, new(), glossary, [], [], false);
        using (var archive = ZipFile.OpenRead(package))
        {
            Program.Check(archive.GetEntry("files/ru.ndjson") is not null && archive.GetEntry("CHANGELOG.md") is not null, "E2E package contents");
            using var stream = archive.GetEntry("files/ru.ndjson")!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
            Program.Check(memory.ToArray().SequenceEqual(await File.ReadAllBytesAsync(ru)), "E2E packaged NDJSON byte match");
        }
        Program.Check(ReleasePackageService.Changes(baseline, baseline).Count == 0 && FileSafetyService.Hash(cn) == cnHash && FileSafetyService.Hash(en) == enHash, "E2E originals and baseline remain unchanged");
        Console.WriteLine("PASS E2E project/import/undo/QA/glossary/review/encoding/search/read-only sources/SQLite/package");
        return vm;
    }
}
