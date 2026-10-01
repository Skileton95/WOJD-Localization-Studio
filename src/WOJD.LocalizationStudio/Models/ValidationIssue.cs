namespace WOJD.LocalizationStudio.Models;

[Flags]
public enum ValidationIssueKind
{
    None = 0,
    Technical = 1 << 0,
    ChineseText = 1 << 1,
    Brackets = 1 << 2,
    Whitespace = 1 << 3,
    Punctuation = 1 << 4,
    SourceCopy = 1 << 5
}

public sealed record ValidationIssue(
    ValidationIssueKind Kind,
    string Message,
    bool IsCritical = false);
