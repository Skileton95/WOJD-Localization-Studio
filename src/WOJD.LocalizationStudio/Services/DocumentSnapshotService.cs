using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class DocumentSnapshotService
{
    public static async Task WriteAsync(LocalizationDocument doc, string path)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(stream, doc.Encoding, leaveOpen: true);
        await DocumentLineWriter.WriteAsync(doc, writer); await writer.FlushAsync(); stream.Flush(true);
        SaveTransactionService.Record("draft-backup", doc.FilePath, path);
    }
}
