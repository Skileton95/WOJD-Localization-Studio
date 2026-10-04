using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed record AiReviewResult(
    bool Changed,
    string Translation,
    string Reason,
    string Model);

public static class AiCorrectionService
{
    private static readonly HttpClient Client =
        new()
        {
            BaseAddress = new Uri("https://api.openai.com/"),
            Timeout = TimeSpan.FromMinutes(3)
        };

    private static string? _sessionApiKey;
    private static string _model = "gpt-5.6-sol";

    public static string Model
    {
        get => _model;
        set => _model = string.IsNullOrWhiteSpace(value)
            ? "gpt-5.6-sol"
            : value.Trim();
    }

    public static bool IsConfigured
        => !string.IsNullOrWhiteSpace(GetApiKey());

    public static void ConfigureSession(
        string apiKey,
        string model)
    {
        _sessionApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
        Model = model;
    }

    public static async Task<AiReviewResult> ReviewAsync(
        LocalizationEntry entry,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key не задан. Укажите ключ в настройке ИИ или переменной окружения OPENAI_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(entry.Original))
        {
            throw new InvalidOperationException(
                "ИИ-проверка отключена для строки без исходного текста: невозможно надёжно проверить перевод без Original.");
        }

        var prompt = BuildPrompt(entry);
        var payload = new
        {
            model = Model,
            store = false,
            instructions =
                "Ты редактор русской локализации MMORPG. Проверяй точность, естественность и игровую терминологию. " +
                "Никогда не удаляй, не переименовывай и не переставляй технические теги и плейсхолдеры, если их порядок важен. " +
                "Верни только JSON без markdown: {\"changed\":true|false,\"translation\":\"...\",\"reason\":\"...\"}. " +
                "Если текущий перевод хороший, changed=false и translation должен быть равен текущему переводу.",
            input = prompt
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/responses");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI API вернул {(int)response.StatusCode}: {ExtractApiError(json)}");
        }

        var outputText = ExtractOutputText(json);
        var parsed = ParseModelJson(outputText);

        if (string.IsNullOrWhiteSpace(parsed.Translation))
        {
            throw new InvalidDataException(
                "ИИ вернул пустой перевод. Изменения не применены.");
        }

        var structure = StructuralQaService.Analyze(
            entry.Original,
            parsed.Translation);

        if (structure.HasIssues)
        {
            throw new InvalidDataException(
                "Предложение ИИ отклонено: оно нарушает теги или плейсхолдеры. " +
                structure.Summary);
        }

        return new AiReviewResult(
            parsed.Changed &&
            !string.Equals(
                parsed.Translation,
                entry.Translation,
                StringComparison.Ordinal),
            parsed.Translation,
            string.IsNullOrWhiteSpace(parsed.Reason)
                ? "ИИ не указал причину изменения."
                : parsed.Reason,
            Model);
    }

    private static string BuildPrompt(LocalizationEntry entry)
        => $"""
           Namespace: {entry.Namespace}
           Key: {entry.Key}

           Китайский оригинал:
           {entry.Original}

           Текущий русский перевод:
           {entry.Translation}

           QA редактора:
           {(string.IsNullOrWhiteSpace(entry.ValidationSummary) ? "нет предупреждений" : entry.ValidationSummary)}

           Проверь перевод. Сохрани смысл и все технические элементы оригинала. Не добавляй пояснений в сам перевод.
           """;

    private static string? GetApiKey()
        => _sessionApiKey
           ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    private static string ExtractOutputText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "OpenAI API не вернул output.");
        }

        var parts = new List<string>();

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    string.Equals(
                        type.GetString(),
                        "output_text",
                        StringComparison.Ordinal) &&
                    part.TryGetProperty("text", out var text))
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        var result = string.Concat(parts).Trim();

        if (result.Length == 0)
            throw new InvalidDataException("OpenAI API вернул пустой ответ.");

        return result;
    }

    private static ParsedAiResult ParseModelJson(string text)
    {
        text = text.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewLine >= 0 && lastFence > firstNewLine)
                text = text[(firstNewLine + 1)..lastFence].Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            throw new InvalidDataException(
                "ИИ не вернул ожидаемый JSON. Изменения не применены.");
        }

        var json = text[start..(end + 1)];
        var result = JsonSerializer.Deserialize<ParsedAiResult>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return result
            ?? throw new InvalidDataException(
                "Не удалось разобрать ответ ИИ.");
    }

    private static string ExtractApiError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? json;
            }
        }
        catch
        {
            // Вернём сырой ответ ниже.
        }

        return json.Length > 800
            ? json[..800] + "…"
            : json;
    }

    private sealed class ParsedAiResult
    {
        public bool Changed { get; set; }
        public string Translation { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
