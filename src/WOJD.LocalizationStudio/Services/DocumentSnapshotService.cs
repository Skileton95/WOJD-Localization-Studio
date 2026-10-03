using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class DocumentSnapshotService
{
    public static async Task WriteAsync(LocalizationDocument doc, string path)
    {
        await using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), doc.Encoding) { NewLine = doc.NewLine };
        var preserved = 0;
        foreach (var row in doc.Entries)
        {
            while (preserved < doc.PreservedLines.Count && doc.PreservedLines[preserved].Line < row.LineNumber) await writer.WriteLineAsync(doc.PreservedLines[preserved++].Text);
            await writer.WriteLineAsync(row.Status == TranslationStatus.Modified ? NdjsonLocalizationAdapter.SerializeEntry(row) : row.RawLine);
        }
        while (preserved < doc.PreservedLines.Count) await writer.WriteLineAsync(doc.PreservedLines[preserved++].Text);
        await writer.FlushAsync(); SaveTransactionService.Record("draft-backup", doc.FilePath, path);
    }
}