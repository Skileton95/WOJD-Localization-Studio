namespace WOJD.LocalizationStudio.Models;

public sealed record NamespaceSummary(string Name, int Total, int Translated, int Untranslated, int Errors)
{
    public string DisplayName => Name.Length == 0 ? "(без Namespace)" : Name;
    public string Counts => $"{Total:N0} / {Translated:N0} / {Untranslated:N0} / {Errors:N0} · {(Total == 0 ? 0 : 100.0 * Translated / Total):F1}%";
}
