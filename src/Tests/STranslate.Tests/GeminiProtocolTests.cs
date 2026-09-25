using STranslate.Plugin;
using STranslate.Plugin.Translate.Gemini;

namespace STranslate.Tests;

public class GeminiProtocolTests
{
    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse")]
    [InlineData("https://generativelanguage.googleapis.com", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse")]
    [InlineData("", "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent?alt=sse")]
    [InlineData("https://proxy.example.com/custom#", "https://proxy.example.com/custom")]
    public void BuildFinalUrl_AppendsStreamGenerateContentPath(string url, string expected)
    {
        Assert.Equal(expected, GeminiProtocol.BuildFinalUrl(url, "gemini-2.5-flash"));
    }

    [Fact]
    public void CreateRequest_MapsSystemPromptToSystemInstruction()
    {
        var request = GeminiProtocol.CreateRequest(
        [
            new PromptItem("system", "sys"),
            new PromptItem("user", "hello"),
            new PromptItem("assistant", "hi"),
        ], 0.5);

        Assert.Equal("sys", request["systemInstruction"]?["parts"]?[0]?["text"]?.ToString());
        var contents = request["contents"]!.AsArray();
        Assert.Equal(2, contents.Count);
        Assert.Equal("user", contents[0]?["role"]?.ToString());
        Assert.Equal("hello", contents[0]?["parts"]?[0]?["text"]?.ToString());
        Assert.Equal("model", contents[1]?["role"]?.ToString());
        Assert.Equal(0.5, request["generationConfig"]?["temperature"]?.GetValue<double>());
    }

    [Theory]
    [InlineData("gemini-2.5-flash", "thinkingBudget", "0")]
    [InlineData("gemini-2.5-flash-lite", "thinkingBudget", "0")]
    [InlineData("gemini-3-flash-preview", "thinkingLevel", "minimal")]
    public void CreateRequest_DisablesThinkingForFlashModels(string model, string key, string expected)
    {
        var request = GeminiProtocol.CreateRequest([new PromptItem("user", "hello")], 0.5, model);

        Assert.Equal(expected, request["generationConfig"]?["thinkingConfig"]?[key]?.ToString());
    }

    [Fact]
    public void CreateRequest_KeepsDefaultThinkingForProModels()
    {
        var request = GeminiProtocol.CreateRequest([new PromptItem("user", "hello")], 0.5, "gemini-2.5-pro");

        Assert.Null(request["generationConfig"]?["thinkingConfig"]);
    }

    [Fact]
    public void ParseStreamLine_ReturnsTextAndSkipsThoughts()
    {
        var line = """data: {"candidates":[{"content":{"parts":[{"text":"thinking","thought":true},{"text":"Hello"}],"role":"model"}}]}""";

        var streamEvent = GeminiProtocol.ParseStreamLine(line);

        Assert.Equal("Hello", streamEvent.TextDelta);
        Assert.Null(streamEvent.ErrorMessage);
    }

    [Fact]
    public void ParseStreamLine_ReturnsError()
    {
        var streamEvent = GeminiProtocol.ParseStreamLine("""data: {"error":{"code":400,"message":"API key not valid"}}""");

        Assert.Equal("API key not valid", streamEvent.ErrorMessage);
    }
}
