using System.Text.Json.Nodes;

namespace CliAccountSwitcher.Api.Models.Responses;

public sealed class CodexResponseResult
{
    public string RawResponseText { get; set; } = "";

    public JsonNode? PayloadNode { get; set; }

    public string OutputText { get; set; } = "";
}
