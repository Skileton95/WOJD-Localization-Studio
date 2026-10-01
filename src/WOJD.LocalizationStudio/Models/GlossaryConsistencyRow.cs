namespace WOJD.LocalizationStudio.Models;

public sealed record GlossaryConsistencyRow(
    string Source,
    string ExpectedTranslation,
    int TotalOccurrences,
    int CorrectOccurrences,
    int MismatchOccurrences,
    string Samples)
{
    public string CoverageText =>
        TotalOccurrences == 0
            ? "—"
            : $"{CorrectOccurrences * 100.0 / TotalOccurrences:0.#}%";
}
