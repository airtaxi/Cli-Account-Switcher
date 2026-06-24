using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class OpenCodeGoLoginResultMessage
{
    public bool IsSuccess { get; init; }

    public string AuthCookie { get; init; } = "";

    public string WorkspaceId { get; init; } = "";

    public OpenCodeGoKeyInfo KeyInfo { get; init; }

    public string ErrorMessage { get; init; } = "";
}
