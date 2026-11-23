using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security.SecretStore;

namespace GoymenERP.Api.Ai.External;

/// <summary>
/// Maps the provider-neutral call (system instruction + user content, text out) onto one wire protocol (Issue #66
/// NS-06). An adapter never decides what may leave, never holds a key between calls and never echoes a provider body.
/// </summary>
public interface IAiExternalProtocolAdapterV1
{
    string Protocol { get; }

    bool RequiresProtocolVersion { get; }

    AiExternalHttpRequestV1 BuildRequest(AiExternalProviderConfigV1 config, ErpSecretV1 apiKey, string systemInstruction, string userContent);

    /// <summary>Returns the model's text, or throws <see cref="AiExternalProviderException"/> with a closed code.</summary>
    string ReadText(AiExternalHttpResponseV1 response);
}

internal static class AiExternalJson
{
    internal const string JsonMediaType = "application/json";

    internal static bool IsSuccess(int statusCode) => statusCode is >= 200 and <= 299;

    internal static JsonElement ParseObject(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
            }

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
        }
    }

    internal static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>OpenAI-style chat completions. The request asks the provider not to store the exchange.</summary>
public sealed class OpenAiChatCompletionsProtocolAdapter : IAiExternalProtocolAdapterV1
{
    public string Protocol => AiExternalProtocolsV1.OpenAiChatCompletions;

    public bool RequiresProtocolVersion => false;

    public AiExternalHttpRequestV1 BuildRequest(AiExternalProviderConfigV1 config, ErpSecretV1 apiKey, string systemInstruction, string userContent)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(apiKey);
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["max_completion_tokens"] = config.MaxOutputTokens,
            ["store"] = false,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemInstruction },
                new JsonObject { ["role"] = "user", ["content"] = userContent },
            },
        };
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Authorization"] = $"Bearer {apiKey.Reveal()}",
            ["Accept"] = AiExternalJson.JsonMediaType,
        };
        return new AiExternalHttpRequestV1(config.Endpoint, headers.AsReadOnly(), body.ToJsonString());
    }

    public string ReadText(AiExternalHttpResponseV1 response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!AiExternalJson.IsSuccess(response.StatusCode))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.HttpStatus);
        }

        var root = AiExternalJson.ParseObject(response.Body);
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
        }

        var choice = choices[0];
        if (string.Equals(AiExternalJson.ReadString(choice, "finish_reason"), "length", StringComparison.Ordinal))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.Truncated);
        }

        if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("message", out var message))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
        }

        if (!string.IsNullOrEmpty(AiExternalJson.ReadString(message, "refusal")))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.ModelRefused);
        }

        var content = AiExternalJson.ReadString(message, "content");
        return string.IsNullOrWhiteSpace(content)
            ? throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse)
            : content;
    }
}

/// <summary>Anthropic-style messages. The protocol version header comes from configuration, never a code default.</summary>
public sealed class AnthropicMessagesProtocolAdapter : IAiExternalProtocolAdapterV1
{
    public string Protocol => AiExternalProtocolsV1.AnthropicMessages;

    public bool RequiresProtocolVersion => true;

    public AiExternalHttpRequestV1 BuildRequest(AiExternalProviderConfigV1 config, ErpSecretV1 apiKey, string systemInstruction, string userContent)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(apiKey);
        if (string.IsNullOrWhiteSpace(config.ProtocolVersion))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.NotReady);
        }

        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["max_tokens"] = config.MaxOutputTokens,
            ["system"] = systemInstruction,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = userContent },
            },
        };
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-api-key"] = apiKey.Reveal(),
            ["anthropic-version"] = config.ProtocolVersion,
            ["Accept"] = AiExternalJson.JsonMediaType,
        };
        return new AiExternalHttpRequestV1(config.Endpoint, headers.AsReadOnly(), body.ToJsonString());
    }

    public string ReadText(AiExternalHttpResponseV1 response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!AiExternalJson.IsSuccess(response.StatusCode))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.HttpStatus);
        }

        var root = AiExternalJson.ParseObject(response.Body);
        if (string.Equals(AiExternalJson.ReadString(root, "stop_reason"), "max_tokens", StringComparison.Ordinal))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.Truncated);
        }

        if (string.Equals(AiExternalJson.ReadString(root, "stop_reason"), "refusal", StringComparison.Ordinal))
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.ModelRefused);
        }

        if (!root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse);
        }

        var text = string.Concat(blocks.EnumerateArray()
            .Where(block => string.Equals(AiExternalJson.ReadString(block, "type"), "text", StringComparison.Ordinal))
            .Select(block => AiExternalJson.ReadString(block, "text") ?? string.Empty));
        return string.IsNullOrWhiteSpace(text)
            ? throw new AiExternalProviderException(AiExternalProviderFailuresV1.MalformedResponse)
            : text;
    }
}
