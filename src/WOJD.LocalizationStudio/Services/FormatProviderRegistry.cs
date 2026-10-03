using System.IO;
using WOJD.LocalizationStudio.Models;
namespace WOJD.LocalizationStudio.Services;
public sealed record FormatProviderInfo(string Id, string Name, string[] Extensions, bool CanRead, bool CanWrite, string Limitation)
{
    public string ExtensionList => string.Join(", ", Extensions);
}
public interface ILocalizationFormatProvider : ILocalizationFileAdapter
{
    FormatProviderInfo Info { get; }
}
public sealed class NdjsonFormatProvider : ILocalizationFormatProvider
{
    private readonly NdjsonLocalizationAdapter _adapter = new();
    public FormatProviderInfo Info { get; }
    public NdjsonFormatProvider(bool fmtstringExport = false) => Info = new(fmtstringExport ? "fmtstring-ndjson" : "ndjson", fmtstringExport ? "fmtstring — проверенный NDJSON-экспорт" : "NDJSON / JSONL", [".ndjson", ".jsonl"], true, true, fmtstringExport ? "Обратная бинарная сборка требует внешнего конвертера." : "");
    public bool CanOpen(string path) => _adapter.CanOpen(path);
    public async Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null)
    { var document = await _adapter.LoadAsync(path, cancellationToken, progress); document.ProviderId = Info.Id; return document; }
    public Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null) => _adapter.SaveAsync(document, cancellationToken, progress);
}
public sealed class BinarySourceProvider : ILocalizationFormatProvider
{
    public FormatProviderInfo Info { get; } = new("binary-unverified", "locres / бинарный fmtstring", [".locres", ".fmtstring"], false, false, "Нужен проверенный внешний конвертер и NDJSON-экспорт. Оригинал сохраняется отдельно.");
    public bool CanOpen(string path) => Info.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null) => throw new NotSupportedException(Info.Limitation);
    public Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null) => throw new NotSupportedException("Запись бинарного оригинала заблокирована. " + Info.Limitation);
}
public sealed class FormatProviderRegistry : ILocalizationFileAdapter
{
    private readonly Dictionary<string, ILocalizationFormatProvider> _providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _declared = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<FormatProviderInfo> Providers => _providers.Values.Select(p => p.Info).ToList();
    public FormatProviderRegistry() { Register(new NdjsonFormatProvider()); Register(new NdjsonFormatProvider(true)); Register(new BinarySourceProvider()); }
    public void Register(ILocalizationFormatProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Info.Id) || !_providers.TryAdd(provider.Info.Id, provider)) throw new ArgumentException("Id провайдера пустой или повторяется.");
    }
    public void DeclareProject(WojdProject project)
    {
        _declared.Clear();
        foreach (var source in project.Sources)
        {
            var id = source.Format switch { "NDJSON" => "ndjson", "fmtstring NDJSON" => "fmtstring-ndjson", "locres" => "binary-unverified", _ => throw new IOException("Неизвестный формат проекта.") };
            _declared[WojdProjectService.Resolve(project, source.Path)] = id;
        }
    }
    public ILocalizationFormatProvider Resolve(string path)
    {
        if (_declared.TryGetValue(Path.GetFullPath(path), out var id))
        {
            var provider = _providers[id]; if (!provider.CanOpen(path)) throw new IOException("Заявленный формат не соответствует расширению; выберите проверенный NDJSON-экспорт.");
            return provider;
        }
        return _providers.Values.FirstOrDefault(p => p.CanOpen(path)) ?? throw new NotSupportedException("Для этого формата нет провайдера.");
    }
    public bool CanOpen(string path) => _providers.Values.Any(p => p.Info.CanRead && p.CanOpen(path));
    public Task<LocalizationDocument> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null)
    {
        var provider = Resolve(path); if (!provider.Info.CanRead) throw new NotSupportedException(provider.Info.Limitation);
        return provider.LoadAsync(path, cancellationToken, progress);
    }
    public Task SaveAsync(LocalizationDocument document, CancellationToken cancellationToken = default, IProgress<FileOperationProgress>? progress = null)
    {
        if (!_providers.TryGetValue(document.ProviderId, out var provider) || !provider.Info.CanWrite || !provider.CanOpen(document.FilePath)) throw new NotSupportedException("Документ не имеет проверенного пишущего провайдера.");
        return provider.SaveAsync(document, cancellationToken, progress);
    }
}
