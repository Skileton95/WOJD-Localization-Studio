using System.IO;
using System.Text;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public static class NdjsonExportService
{
    public static async Task ExportAsync(IEnumerable<LocalizationEntry> entries, string path, CancellationToken cancellationToken = default)
    {
        if (File.Exists(path)) throw new IOException("Экспорт не перезаписывает существующие файлы. Выберите новое имя.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, true))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 256 * 1024, leaveOpen: true);
                foreach (var entry in entries) { cancellationToken.ThrowIfCancellationRequested(); await writer.WriteLineAsync(NdjsonLocalizationAdapter.SerializeEntry(entry).AsMemory(), cancellationToken); }
                await writer.FlushAsync(cancellationToken); stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path, false);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
