using System.Text.Json.Nodes;

namespace STranslate.Plugin.Translate.Gemini;

internal static class GeminiProtocol
{
    private const string DefaultUrl = "https://generativelanguage.googleapis.com/";

    /// <summary>
    /// 构建 streamGenerateContent 地址
    /// </summary>
    /// <remarks>
    /// 以 "#" 结尾时强制使用该地址（用于自定义代理），否则拼接为
    /// {url}/v1beta/models/{model}:streamGenerateContent?alt=sse
    /// </remarks>
    internal static string BuildFinalUrl(string url, string model)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = DefaultUrl;

        url = url.Trim();
        if (url.EndsWith('#'))
            return url.TrimEnd('#').TrimEnd();

        var baseUrl = url.TrimEnd('/');
        if (!baseUrl.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1beta";

        return $"{baseUrl}/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse";
    }

    internal static JsonObject CreateRequest(IReadOnlyCollection<PromptItem> messages, double temperature)
    {
        var systemTexts = messages
            .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content)
            .ToList();

        var contents = new JsonArray();
        foreach (var message in messages.Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)))
        {
            // Gemini 仅支持 user / model 两种角色
            var role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(message.Role, "model", StringComparison.OrdinalIgnoreCase)
                ? "model"
                : "user";

            contents.Add(new JsonObject
            {
                ["role"] = role,
                ["parts"] = new JsonArray(new JsonObject { ["text"] = message.Content })
            });
        }

        var request = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["temperature"] = temperature }
        };

        if (systemTexts.Count > 0)
        {
            request["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = string.Join("\n\n", systemTexts) })
            };
        }

        return request;
    }

    internal static GeminiStreamEvent ParseStreamLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return default;

        var payload = line.StartsWith("data:", StringComparison.Ordinal)
            ? line["data:".Length..].Trim()
            : line.Trim();

        if (payload.Length == 0 || !payload.StartsWith('{'))
            return default;

        JsonNode? parsedData;
        try
        {
            parsedData = JsonNode.Parse(payload);
        }
        catch
        {
            return default;
        }

        if (parsedData is null)
            return default;

        var errorMessage = parsedData["error"]?["message"]?.ToString();
        if (!string.IsNullOrWhiteSpace(errorMessage))
            return new GeminiStreamEvent(null, errorMessage);

        if (parsedData["candidates"] is not JsonArray { Count: > 0 } candidates ||
            candidates[0]?["content"]?["parts"] is not JsonArray parts)
            return default;

        var text = string.Concat(parts
            // 跳过思考摘要
            .Where(p => p?["thought"]?.ToString() != "true")
            .Select(p => p?["text"]?.ToString()));

        return string.IsNullOrEmpty(text)
            ? default
            : new GeminiStreamEvent(text, null);
    }
}

internal readonly record struct GeminiStreamEvent(string? TextDelta, string? ErrorMessage);
