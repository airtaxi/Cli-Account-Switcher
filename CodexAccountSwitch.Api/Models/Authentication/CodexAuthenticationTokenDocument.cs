namespace CodexAccountSwitch.Api.Models.Authentication;

public sealed class CodexAuthenticationTokenDocument
{
    public string IdentityToken { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public string AccountIdentifier { get; set; } = "";
}
