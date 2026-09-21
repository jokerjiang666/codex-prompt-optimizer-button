using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class OpenAiCompatibleProvider : IOptimizerProvider
{
    private readonly AppSettings _settings;
    private readonly string _apiKey;

    public OpenAiCompatibleProvider(AppSettings settings, string apiKey)
    {
        _settings = settings;
        _apiKey = apiKey;
    }

    public async Task<string> OptimizeAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiBaseUrl))
            throw new InvalidOperationException("请先配置 API Base URL。");
        if (string.IsNullOrWhiteSpace(_settings.Model))
            throw new InvalidOperationException("请先配置模型名称。");
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("请先配置 API Key。");

        var endpoint = BuildEndpoint(_settings.ApiBaseUrl);
        var prompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? AppSettings.DefaultOptimizationPrompt
            : systemPrompt.Trim();

        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 5, 300)));

        var useResponses = endpoint.EndsWith("/responses", StringComparison.OrdinalIgnoreCase);
        object body = useResponses
            ? BuildResponsesBody(prompt, userPrompt)
            : BuildChatBody(prompt, userPrompt);

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content, linked.Token);
        var responseText = await response.Content.ReadAsStringAsync(linked.Token);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API 请求失败：HTTP {(int)response.StatusCode}。");

        var result = useResponses ? ReadResponsesText(responseText) : ReadChatText(responseText);
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("API 未返回可用的优化文本。");
        return result.Trim();
    }

    public static async Task<IReadOnlyList<string>> GetModelsAsync(
        string apiBaseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("请先配置 API Base URL。");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("请先配置 API Key。");

        var endpoint = BuildModelsEndpoint(apiBaseUrl);
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync(endpoint, linked.Token);
        var responseText = await response.Content.ReadAsStringAsync(linked.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取模型失败：HTTP {(int)response.StatusCode}。");

        using var doc = JsonDocument.Parse(responseText);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("模型列表响应不是 OpenAI 兼容格式。");
        }

        return data.EnumerateArray()
            .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private object BuildChatBody(string prompt, string text)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _settings.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = text }
            }
        };

        if (!string.IsNullOrWhiteSpace(_settings.ReasoningEffort))
            body["reasoning_effort"] = _settings.ReasoningEffort;
        return body;
    }

    private object BuildResponsesBody(string prompt, string text)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _settings.Model,
            ["instructions"] = prompt,
            ["input"] = text
        };

        if (!string.IsNullOrWhiteSpace(_settings.ReasoningEffort))
            body["reasoning"] = new { effort = _settings.ReasoningEffort };
        return body;
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return url + "/chat/completions";
    }

    private static string BuildModelsEndpoint(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/chat/completions".Length];
        else if (url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/responses".Length];

        return url.TrimEnd('/') + "/models";
    }

    private static string? ReadChatText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() : null;
    }

    private static string? ReadResponsesText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString();
            }
        }

        return null;
    }
}
