using WOJD.LocalizationStudio.Services;
namespace WOJD.LocalizationStudio.ViewModels;
public sealed partial class MainViewModel
{
    public string SelectedContextText => SelectedEntry is null || ActiveDocument is null ? "Выберите строку." :
        string.Join("\n", ActiveDocument.Entries.Skip(Math.Max(0, SelectedEntry.Index - 3)).Take(5).Select(e => $"{(ReferenceEquals(e, SelectedEntry) ? "● " : "")}{e.Key}: {(e.Original.Length > 90 ? e.Original[..90] + "…" : e.Original)}"));
}
