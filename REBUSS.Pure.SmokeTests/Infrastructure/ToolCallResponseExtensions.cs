using System.Text.Json;

namespace REBUSS.Pure.SmokeTests.Infrastructure;

/// <summary>
/// Helper extension methods for parsing MCP tool-call responses in contract tests.
/// </summary>
public static class ToolCallResponseExtensions
{
    /// <summary>
    /// Extracts the tool result <c>content[0].text</c> as plain text.
    /// Throws if the response indicates an error.
    /// </summary>
    public static string GetToolText(this JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");

        if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean())
        {
            var errorText = TryGetFirstText(result) ?? "unknown";
            throw new InvalidOperationException($"Tool returned error: {errorText}");
        }

        return TryGetFirstText(result)
            ?? throw new InvalidOperationException("Tool response has no text content.");
    }

    /// <summary>
    /// Extracts the tool result <c>content[0].text</c> as a parsed <see cref="JsonElement"/>.
    /// Throws if the response indicates an error.
    /// </summary>
    public static JsonElement GetToolContent(this JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");

        if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean())
        {
            var errorText = TryGetFirstText(result) ?? "unknown";
            throw new InvalidOperationException($"Tool returned error: {errorText}");
        }

        var text = TryGetFirstText(result)
            ?? throw new InvalidOperationException("Tool response has no text content to parse as JSON.");
        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>
    /// Returns true if the tool response indicates an error.
    /// </summary>
    public static bool IsToolError(this JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        return result.TryGetProperty("isError", out var isError) && isError.GetBoolean();
    }

    /// <summary>
    /// Gets the error message text from an error tool response.
    /// </summary>
    public static string GetToolErrorMessage(this JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        return TryGetFirstText(result) ?? string.Empty;
    }

    private static string? TryGetFirstText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;

        if (content.GetArrayLength() == 0)
            return null;

        var first = content[0];
        if (!first.TryGetProperty("text", out var textElement))
            return null;

        return textElement.GetString();
    }
}
