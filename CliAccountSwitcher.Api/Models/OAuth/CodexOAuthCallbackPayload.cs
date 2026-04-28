namespace CliAccountSwitcher.Api.Models.OAuth;

public sealed class CodexOAuthCallbackPayload
{
    public string AuthorizationCode { get; set; } = "";

    public string State { get; set; } = "";

    public string Error { get; set; } = "";

    public string ErrorDescription { get; set; } = "";

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
}
