using System.Text.Json.Nodes;

namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderResponseStreamEvent
{
    public string EventName { get; set; } = "";

    public string Data { get; set; } = "";

    public JsonNode? PayloadNode { get; set; }

    public bool IsTerminal { get; set; }
}
