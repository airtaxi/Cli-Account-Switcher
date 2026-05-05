using System;
using System.Text.Json.Nodes;

namespace CliAccountSwitcher.Api.Providers.Codex.Models.Responses;

public sealed class CodexResponseRequest
{
    public string Model { get; set; } = "";

    public string? Instructions { get; set; }

    public string? InputText { get; set; }

    public JsonNode? InputPayload { get; set; }

    public bool Stream { get; set; }

    public bool Store { get; set; }

    public JsonObject AdditionalProperties { get; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model)) throw new CodexApiException("The response request requires a model.");
        if (string.IsNullOrWhiteSpace(InputText) && InputPayload is null) throw new CodexApiException("The response request requires either InputText or InputPayload.");
    }

    internal JsonObject CreatePayload(bool forceStreaming)
    {
        Validate();

        var payloadObject = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = forceStreaming || Stream,
            ["store"] = Store
        };

        payloadObject["instructions"] = string.IsNullOrWhiteSpace(Instructions) ? CodexApiConventions.DefaultResponsesInstructionsText : Instructions;
        payloadObject["input"] = InputPayload?.DeepClone() ?? CreateTextInputPayload(InputText!);

        foreach (var additionalProperty in AdditionalProperties)
        {
            payloadObject[additionalProperty.Key] = additionalProperty.Value?.DeepClone();
        }

        return payloadObject;
    }

    private static JsonArray CreateTextInputPayload(string inputText)
        => [
            new JsonObject
            {
                ["type"] = "message",
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = inputText
                    }
                }
            }
        ];
}
