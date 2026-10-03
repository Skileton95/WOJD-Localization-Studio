using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class DocumentLineWriter
{
    public static async Task<List<(LocalizationEntry Entry, string RawLine)>> WriteAsync(LocalizationDocument document, StreamWriter writer, CancellationToken cancellation = default, IProgress<FileOperationProgress>? progress = null)
    {
        var changes = new List<(LocalizationEntry, string)>(); var preserved = 0; var previousLine = 0; var written = 0;
        async Task Line(int physical, string text)
        {
            cancellation.ThrowIfCancellationRequested();
            if (previousLine > 0) await writer.WriteAsync((document.LineEndings.GetValueOrDefault(previousLine) ?? document.NewLine).AsMemory(), cancellation);
            await writer.WriteAsync(text.AsMemory(), cancellation); previousLine = physical;
        }
        foreach (var entry in document.Entries)
        {
            while (preserved < document.PreservedLines.Count && document.PreservedLines[preserved].Line < entry.LineNumber)
            { var line = document.PreservedLines[preserved++]; await Line(line.Line, line.Text); }
            var raw = entry.Status == TranslationStatus.Modified ? NdjsonLocalizationAdapter.SerializeEntry(entry) : entry.RawLine;
            await Line(entry.LineNumber, raw);
            if (entry.Status == TranslationStatus.Modified) changes.Add((entry, raw));
            if ((++written & 4095) == 0) progress?.Report(new(100.0 * written / Math.Max(1, document.Entries.Count), written, "Сохранение"));
        }
        while (preserved < document.PreservedLines.Count) { var line = document.PreservedLines[preserved++]; await Line(line.Line, line.Text); }
        if (previousLine > 0 && document.HasFinalNewLine) await writer.WriteAsync((document.LineEndings.GetValueOrDefault(previousLine) ?? document.NewLine).AsMemory(), cancellation);
        return changes;
    }
}
