using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using CodexAccountSwitch.Api.Infrastructure;
using CodexAccountSwitch.Api.Models;
using CodexAccountSwitch.Api.Models.Authentication;
using CodexAccountSwitch.Api.Models.Responses;

namespace CodexAccountSwitch.Api;

public sealed class CodexResponsesClient(HttpClient httpClient, CodexRequestMessageFactory codexRequestMessageFactory)
{
    public Task<CodexResponseResult> CreateResponseAsync(CodexAuthenticationDocument codexAuthenticationDocument, CodexResponseRequest codexResponseRequest, bool useCompactEndpoint = false, CancellationToken cancellationToken = default)
        => CreateResponseAsync(codexAuthenticationDocument.CreateRequestContext(), codexResponseRequest, useCompactEndpoint, cancellationToken);

    public async Task<CodexResponseResult> CreateResponseAsync(CodexRequestContext codexRequestContext, CodexResponseRequest codexResponseRequest, bool useCompactEndpoint = false, CancellationToken cancellationToken = default)
    {
        EnsurePromptResponsesEndpoint(useCompactEndpoint);

        var rawResponseTextStringBuilder = new StringBuilder();
        var outputTextParts = new List<string>();
        var outputTextDeltaStringBuilder = new StringBuilder();
        JsonNode? completedResponsePayloadNode = null;

        await foreach (var codexResponseStreamEvent in StreamResponseAsync(codexRequestContext, codexResponseRequest, false, cancellationToken))
        {
            AppendRawStreamEvent(rawResponseTextStringBuilder, codexResponseStreamEvent);
            if (TryGetCompletedResponsePayloadNode(codexResponseStreamEvent.PayloadNode, out var currentCompletedResponsePayloadNode)) completedResponsePayloadNode = currentCompletedResponsePayloadNode;
            if (TryGetCompletedOutputText(codexResponseStreamEvent.PayloadNode, out var completedOutputText)) outputTextParts.Add(completedOutputText);
            else if (TryGetOutputTextDelta(codexResponseStreamEvent.PayloadNode, out var outputTextDelta)) outputTextDeltaStringBuilder.Append(outputTextDelta);
        }

        var outputText = outputTextParts.Count > 0 ? string.Join("\n", outputTextParts) : outputTextDeltaStringBuilder.ToString();
        var payloadNode = CreateUnifiedResponsePayloadNode(completedResponsePayloadNode, outputText);
        return new CodexResponseResult
        {
            RawResponseText = payloadNode?.ToJsonString() ?? rawResponseTextStringBuilder.ToString(),
            PayloadNode = payloadNode,
            OutputText = outputText
        };
    }

    public IAsyncEnumerable<CodexResponseStreamEvent> StreamResponseAsync(CodexAuthenticationDocument codexAuthenticationDocument, CodexResponseRequest codexResponseRequest, bool useCompactEndpoint = false, CancellationToken cancellationToken = default)
        => StreamResponseAsync(codexAuthenticationDocument.CreateRequestContext(), codexResponseRequest, useCompactEndpoint, cancellationToken);

    public async IAsyncEnumerable<CodexResponseStreamEvent> StreamResponseAsync(CodexRequestContext codexRequestContext, CodexResponseRequest codexResponseRequest, bool useCompactEndpoint = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsurePromptResponsesEndpoint(useCompactEndpoint);

        var requestPath = useCompactEndpoint ? CodexApiConventions.CompactResponsesPath : CodexApiConventions.ResponsesPath;
        using var httpRequestMessage = CreateJsonRequestMessage(codexRequestContext, codexResponseRequest, requestPath, true);
        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var errorResponseText = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            throw new CodexApiException("The Codex streaming request failed.", httpResponseMessage.StatusCode, errorResponseText);
        }

        await using var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);
        using var streamReader = new StreamReader(responseStream, Encoding.UTF8);

        var eventName = "";
        var dataLines = new List<string>();

        while (true)
        {
            var lineText = await streamReader.ReadLineAsync(cancellationToken);
            if (lineText is null) break;

            if (lineText.Length == 0)
            {
                var streamEvent = CreateStreamEvent(eventName, dataLines);
                eventName = "";
                dataLines.Clear();
                if (streamEvent is null) continue;

                yield return streamEvent;
                if (streamEvent.IsTerminal) yield break;
                continue;
            }

            if (lineText.StartsWith("event:")) eventName = lineText["event:".Length..].Trim();
            else if (lineText.StartsWith("data:")) dataLines.Add(lineText["data:".Length..].TrimStart());
        }

        var trailingEvent = CreateStreamEvent(eventName, dataLines);
        if (trailingEvent is not null) yield return trailingEvent;
    }

    private static void EnsurePromptResponsesEndpoint(bool useCompactEndpoint)
    {
        if (useCompactEndpoint) throw new CodexApiException("The compact endpoint is reserved for conversation compaction and is not supported for prompt response requests.");
    }

    private HttpRequestMessage CreateJsonRequestMessage(CodexRequestContext codexRequestContext, CodexResponseRequest codexResponseRequest, string requestPath, bool forceStreaming)
    {
        var requestBody = codexResponseRequest.CreatePayload(forceStreaming).ToJsonString();
        var stringContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var acceptHeaderValue = forceStreaming ? "text/event-stream" : "application/json";
        var httpRequestMessage = codexRequestMessageFactory.CreateCodexApiRequest(HttpMethod.Post, requestPath, codexRequestContext, stringContent, acceptHeaderValue);
        httpRequestMessage.Headers.ConnectionClose = !forceStreaming;
        if (forceStreaming) httpRequestMessage.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        return httpRequestMessage;
    }

    private static void AppendRawStreamEvent(StringBuilder rawResponseTextStringBuilder, CodexResponseStreamEvent codexResponseStreamEvent)
    {
        if (!string.IsNullOrWhiteSpace(codexResponseStreamEvent.EventName)) rawResponseTextStringBuilder.Append("event: ").AppendLine(codexResponseStreamEvent.EventName);
        if (!string.IsNullOrWhiteSpace(codexResponseStreamEvent.Data)) rawResponseTextStringBuilder.Append("data: ").AppendLine(codexResponseStreamEvent.Data);
        if (!string.IsNullOrWhiteSpace(codexResponseStreamEvent.EventName) || !string.IsNullOrWhiteSpace(codexResponseStreamEvent.Data)) rawResponseTextStringBuilder.AppendLine();
    }

    private static CodexResponseStreamEvent? CreateStreamEvent(string eventName, List<string> dataLines)
    {
        if (string.IsNullOrWhiteSpace(eventName) && dataLines.Count == 0) return null;

        var dataText = string.Join("\n", dataLines);
        return new CodexResponseStreamEvent
        {
            EventName = eventName,
            Data = dataText,
            PayloadNode = dataText == "[DONE]" || string.IsNullOrWhiteSpace(dataText) ? null : JsonNode.Parse(dataText),
            IsTerminal = dataText == "[DONE]"
        };
    }

    private static JsonNode? CreateUnifiedResponsePayloadNode(JsonNode? completedResponsePayloadNode, string outputText)
    {
        if (completedResponsePayloadNode is null && string.IsNullOrWhiteSpace(outputText)) return null;

        var payloadObject = new JsonObject
        {
            ["output_text"] = outputText
        };
        if (completedResponsePayloadNode is not null) payloadObject["response"] = completedResponsePayloadNode.DeepClone();
        return payloadObject;
    }

    private static bool TryGetCompletedResponsePayloadNode(JsonNode? payloadNode, out JsonNode? completedResponsePayloadNode)
    {
        completedResponsePayloadNode = null;
        if (payloadNode is not JsonObject payloadObject) return false;
        if (!string.Equals(payloadObject["type"]?.ToString(), "response.completed", System.StringComparison.Ordinal)) return false;
        if (payloadObject["response"] is null) return false;

        completedResponsePayloadNode = payloadObject["response"]!.DeepClone();
        return true;
    }

    private static bool TryGetCompletedOutputText(JsonNode? payloadNode, out string completedOutputText)
    {
        completedOutputText = "";
        if (payloadNode is not JsonObject payloadObject) return false;
        if (!string.Equals(payloadObject["type"]?.ToString(), "response.output_text.done", System.StringComparison.Ordinal)) return false;

        completedOutputText = payloadObject["text"]?.ToString() ?? "";
        return !string.IsNullOrWhiteSpace(completedOutputText);
    }

    private static bool TryGetOutputTextDelta(JsonNode? payloadNode, out string outputTextDelta)
    {
        outputTextDelta = "";
        if (payloadNode is not JsonObject payloadObject) return false;
        if (!string.Equals(payloadObject["type"]?.ToString(), "response.output_text.delta", System.StringComparison.Ordinal)) return false;

        outputTextDelta = payloadObject["delta"]?.ToString() ?? "";
        return !string.IsNullOrWhiteSpace(outputTextDelta);
    }

    private static string ExtractOutputText(JsonNode? payloadNode)
    {
        if (payloadNode is not JsonObject rootObject) return "";
        if (rootObject["output_text"] is JsonValue outputTextValue) return outputTextValue.ToString();

        if (rootObject["output"] is JsonArray outputArray)
        {
            var stringBuilder = new StringBuilder();
            foreach (var outputItem in outputArray)
            {
                if (outputItem is not JsonObject outputObject) continue;
                if (outputObject["content"] is not JsonArray contentArray) continue;

                foreach (var contentItem in contentArray)
                {
                    if (contentItem is not JsonObject contentObject) continue;

                    var textValue = contentObject["text"]?.ToString() ?? contentObject["output_text"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(textValue)) continue;
                    if (stringBuilder.Length > 0) stringBuilder.AppendLine();
                    stringBuilder.Append(textValue);
                }
            }

            return stringBuilder.ToString();
        }

        if (rootObject["choices"] is JsonArray choiceArray && choiceArray.Count > 0)
        {
            var messageContent = choiceArray[0]?["message"]?["content"]?.ToString();
            return messageContent ?? "";
        }

        return "";
    }
}
