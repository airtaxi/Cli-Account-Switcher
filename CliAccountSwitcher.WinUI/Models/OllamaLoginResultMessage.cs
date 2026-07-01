namespace CliAccountSwitcher.WinUI.Models;

public sealed class OllamaLoginResultMessage
{
    public bool IsSuccess { get; init; }

    public string AuthCookie { get; init; } = "";

    public string UserName { get; init; } = "";

    public string EmailAddress { get; init; } = "";

    public string ErrorMessage { get; init; } = "";
}