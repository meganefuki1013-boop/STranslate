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

    internal static JsonObject CreateRequest(IReadOnlyCollection<PromptItem> messages, double temperature, string? model = null)
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

        var generationConfig = new JsonObject { ["temperature"] = temperature };
        if (CreateThinkingConfig(model) is { } thinkingConfig)
            generationConfig["thinkingConfig"] = thinkingConfig;

        var request = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = generationConfig
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

    /// <summary>
    /// 翻译无需推理，尽量关闭思考以降低首字延迟
    /// </summary>
    /// <remarks>
    /// 2.5 Flash 系列可用 thinkingBudget=0 关闭；Gemini 3 Flash 系列使用 thinkingLevel=minimal；
    /// Pro 系列不支持关闭思考，保持默认。
    /// </remarks>
    internal static JsonObject? CreateThinkingConfig(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        var name = model.Trim().ToLowerInvariant();
        if (name.Contains("gemini-2.5-flash"))
            return new JsonObject { ["thinkingBudget"] = 0 };

        if (name.StartsWith("gemini-3") && name.Contains("flash"))
            return new JsonObject { ["thinkingLevel"] = "minimal" };

        return null;
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
