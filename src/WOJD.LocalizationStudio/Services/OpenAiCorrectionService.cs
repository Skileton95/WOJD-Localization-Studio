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
        Timeout = TimeSpan.FromSeconds(45)
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

    public async Task<string> CorrectAsync(
        LocalizationEntry entry,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Не указан OPENAI_API_KEY.");

        var instructions =
            """
            Ты исправляешь русскую локализацию MMORPG Zhu Xian World.
            Твоя задача — исправить ТОЛЬКО текущий русский перевод так, чтобы он сохранял смысл китайского оригинала
            и содержал все технические элементы оригинала в правильном количестве и логичном месте:
            плейсхолдеры вида {0}, {1}, %s, %d, переносы строк и XML/HTML-подобные теги.
            Никогда не переводи, не переименовывай и не изменяй технические теги и плейсхолдеры.
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

            Ошибка проверки:
            {entry.ValidationSummary}
            """;

        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            store = false,
            instructions,
            input,
            max_output_tokens = 2048
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

        using var json = JsonDocument.Parse(responseText);
        if (!json.RootElement.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenAI API не вернул текстовый ответ.");

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

                var value = text.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        throw new InvalidOperationException("ChatGPT не вернул исправленный перевод.");
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
