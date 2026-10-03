namespace WOJD.LocalizationStudio.Models;
public sealed record EditHistoryRow(string FilePath, string Namespace, string Key, string Field, string Before, string After, string? SourceHash = null);
public sealed record EditHistoryItem(Guid Id, DateTime AtUtc, string Author, string Operation, List<EditHistoryRow> Rows)
{
    public int Count => Rows.Count;
}