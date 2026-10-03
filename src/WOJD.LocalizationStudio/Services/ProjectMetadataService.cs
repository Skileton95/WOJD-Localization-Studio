using System.IO;
using System.Text.Json;
namespace WOJD.LocalizationStudio.Services;
public static class ProjectMetadataService
{
    public static T Load<T>(string path, Func<T> empty)
    {
        if (!File.Exists(path)) return empty();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new IOException("Пустые metadata."); }
        catch (JsonException e) { throw new IOException("Metadata повреждены; файл сохранён: " + path, e); }
    }
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Replace(temp, path, path + ".last-good.bak"); else File.Move(temp, path);
    }
}
