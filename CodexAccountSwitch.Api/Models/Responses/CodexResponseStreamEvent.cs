using System.Text.Json.Nodes;

namespace CodexAccountSwitch.Api.Models.Responses;

public sealed class CodexResponseStreamEvent
{
    public string EventName { get; set; } = "";

    public string Data { get; set; } = "";

    public JsonNode? PayloadNode { get; set; }

    public bool IsTerminal { get; set; }
}
