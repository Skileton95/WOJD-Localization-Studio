using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WOJD.LocalizationStudio.Models;

namespace WOJD.LocalizationStudio.Services;

public sealed class OpenAiCorrectionService
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    private const string Model = "chat-latest";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private string? _sessionApiKey;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(GetApiKey());

    public void SetApiKey(string apiKey, bool remember)
    {
        var trimmed = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("API-ключ пуст.", nameof(apiKey));

        _sessionApiKey = trimmed;
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", trimmed, EnvironmentVariableTarget.Process);

        if (remember)
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", trimmed, EnvironmentVariableTarget.User);
    }

    public Task<string> CorrectAsync(
        LocalizationEntry entry,
        CancellationToken cancellationToken = default)
    {
        const string instructions =
            """
            Ты исправляешь русскую локализацию MMORPG Zhu Xian World.
            Исправь текущий русский перевод, сохранив смысл китайского оригинала.
            Все технические элементы оригинала должны присутствовать в правильном количестве и логичном месте:
            {0}, {1}, %s, %d, переносы строк и XML/HTML-подобные теги.
            Никогда не переводи, не переименовывай и не меняй технические теги и плейсхолдеры.
            Исправляй также оставшийся китайский текст, очевидные ошибки скобок, пробелов и пунктуации.
            Не добавляй пояснений, кавычек, Markdown или комментариев.
            Верни только готовую исправленную русскую строку.
            """;

        var input =
            $"""
            Namespace: {entry.Namespace}
            Key: {entry.Key}

            Китайский оригинал:
            {entry.Source}

            Текущий русский перевод:
            {entry.Translation}

            Ошибки проверки:
            {entry.ValidationSummary}
            """;

        return SendTextAsync(instructions, input, 2048, cancellationToken);
    }

    public Task<string> ExplainAsync(
        LocalizationEntry entry,
        CancellationToken cancellationToken = default)
    {
        const string instructions =
            """
            Ты проверяешь русскую локализацию MMORPG Zhu Xian World.
            Коротко и конкретно объясни, что не так в текущем переводе и как это исправить.
            Укажи назначение потерянных/лишних плейсхолдеров и тегов, если это можно понять из строки.
            Не переписывай весь перевод без необходимости.
            Ответ дай на русском языке, без Markdown-заголовков.
            """;

        var input =
            $"""
            Namespace: {entry.Namespace}
            Key: {entry.Key}

            Китайский оригинал:
            {entry.Source}

            Русский перевод:
            {entry.Translation}

            Найденные программой проблемы:
            {entry.ValidationSummary}
            """;

        return SendTextAsync(instructions, input, 1800, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> SuggestVariantsAsync(
        LocalizationEntry entry,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 2, 5);

        const string instructions =
            """
            Ты профессионально локализуешь MMORPG Zhu Xian World с китайского на русский.
            Предлагай естественные игровые формулировки на русском.
            Технические элементы {0}, {1}, %s, %d, переносы строк и XML/HTML-подобные теги
            должны быть сохранены без изменений и в логичных местах.
            Не транслитерируй без необходимости и не добавляй информацию, которой нет в оригинале.
            Верни только JSON-массив строк, без Markdown и пояснений.
            """;

        var input =
            $"""
            Namespace: {entry.Namespace}
            Key: {entry.Key}

            Китайский оригинал:
            {entry.Source}

            Текущий перевод, если он уже есть:
            {entry.Translation}

            Предложи {count} разных качественных варианта русского перевода.
            """;

        var raw = await SendTextAsync(instructions, input, 2600, cancellationToken);
        var variants = ParseStringArray(raw)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(count)
            .ToList();

        if (variants.Count == 0)
            throw new InvalidOperationException("ChatGPT не вернул варианты перевода.");

        return variants;
    }

    private async Task<string> SendTextAsync(
        string instructions,
        string input,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Не указан OPENAI_API_KEY.");

        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            store = false,
            instructions,
            input,
            max_output_tokens = maxOutputTokens
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd("WOJD-Localization-Studio");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OpenAI API вернул ошибку {(int)response.StatusCode}: {ExtractApiError(responseText)}");

        var text = ExtractOutputText(responseText);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("ChatGPT не вернул текстовый ответ.");

        return text.Trim('\r', '\n');
    }

    private static string ExtractOutputText(string responseText)
    {
        using var json = JsonDocument.Parse(responseText);

        if (!json.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var type) ||
                    type.GetString() != "output_text" ||
                    !part.TryGetProperty("text", out var text))
                    continue;

                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseStringArray(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
            trimmed = trimmed[start..(end + 1)];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
        }
        catch (JsonException)
        {
            return raw
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.TrimStart('-', '•', '1', '2', '3', '4', '5', '.', ')', ' '))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
    }

    private string? GetApiKey() =>
        !string.IsNullOrWhiteSpace(_sessionApiKey)
            ? _sessionApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Process)
              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);

    private static string ExtractApiError(string responseText)
    {
        try
        {
            using var json = JsonDocument.Parse(responseText);
            if (json.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString() ?? "Неизвестная ошибка.";
        }
        catch
        {
            // Ignore malformed error payload.
        }

        return string.IsNullOrWhiteSpace(responseText)
            ? "Неизвестная ошибка."
            : responseText.Trim();
    }
}
