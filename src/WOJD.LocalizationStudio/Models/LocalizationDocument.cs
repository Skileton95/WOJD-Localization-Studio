using System.Text;
namespace WOJD.LocalizationStudio.Models;
public sealed record PreservedLine(int Line, string Text);
public sealed record LoadIssue(int Line, string Message);
public sealed class LocalizationDocument
{
    public string FilePath { get; init; } = "";
    public string? DiskHash { get; set; }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false, true);
    public string NewLine { get; set; } = Environment.NewLine;
    public List<LocalizationEntry> Entries { get; } = [];
    public List<PreservedLine> PreservedLines { get; } = [];
    public List<LoadIssue> LoadIssues { get; } = [];
}