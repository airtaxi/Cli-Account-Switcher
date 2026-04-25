namespace CodexAccountSwitch.Api.Models;

public sealed class CodexRequestContext
{
    public required string AccessToken { get; init; }

    public required string AccountId { get; init; }
}
