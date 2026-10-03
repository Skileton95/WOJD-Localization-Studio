using System.Text;
namespace WOJD.LocalizationStudio.Models;
public sealed record PreservedLine(int Line, string Text);
public sealed record LoadIssue(int Line, string Message);
public sealed class LocalizationDocument
{
    public bool IsReadOnly { get; set; }
    public bool HasFinalNewLine { get; set; } = true;
    public Dictionary<int, string> LineEndings { get; } = [];
    public string ProviderId { get; set; } = "ndjson";
    public string FilePath { get; init; } = "";
    public DateTime DiskLastWriteUtc { get; set; }
    public long DiskLength { get; set; }
    public string? DiskHash { get; set; }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false, true);
    public string NewLine { get; set; } = Environment.NewLine;
    public List<LocalizationEntry> Entries { get; } = [];
    public List<PreservedLine> PreservedLines { get; } = [];
    public List<LoadIssue> LoadIssues { get; } = [];
}