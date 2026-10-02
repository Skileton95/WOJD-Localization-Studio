using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class LocalizationAdapterRegistry : ILocalizationFileAdapter
{
    private readonly IReadOnlyList<ILocalizationFileAdapter> _adapters;

    public LocalizationAdapterRegistry()
    {
        _adapters =
        [
            new NdjsonLocalizationAdapter(),
            new LocresLocalizationAdapter()
        ];
    }

    public string Id => "registry";
    public string DisplayName => "All supported formats";

    public IReadOnlyCollection<string> Extensions
        => _adapters
            .SelectMany(x => x.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ILocalizationFileAdapter> Adapters
        => _adapters;

    public bool CanOpen(string path)
        => _adapters.Any(x => x.CanOpen(path));

    public Task<LocalizationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var adapter =
            _adapters.FirstOrDefault(x => x.CanOpen(path))
            ?? throw new NotSupportedException(
                $"Формат файла не поддерживается: {Path.GetExtension(path)}");

        return adapter.LoadAsync(path, cancellationToken);
    }

    public Task SaveAsync(
        LocalizationDocument document,
        CancellationToken cancellationToken = default)
    {
        var adapter =
            _adapters.FirstOrDefault(
                x =>
                    string.Equals(
                        x.Id,
                        document.AdapterId,
                        StringComparison.OrdinalIgnoreCase))
            ?? _adapters.FirstOrDefault(
                x => x.CanOpen(document.FilePath))
            ?? throw new NotSupportedException(
                $"Не найден адаптер для {document.FilePath}");

        return adapter.SaveAsync(document, cancellationToken);
    }
}
