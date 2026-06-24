using System.Collections.Generic;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class OpenCodeGoAccountStoreDocument
{
    public int Version { get; set; } = 1;

    public List<OpenCodeGoAccount> Accounts { get; set; } = [];

    public string ActiveAccountIdentifier { get; set; } = "";
}
