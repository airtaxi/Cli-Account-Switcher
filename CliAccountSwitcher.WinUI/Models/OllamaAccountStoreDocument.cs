using System.Collections.Generic;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class OllamaAccountStoreDocument
{
    public int Version { get; set; } = 1;

    public List<OllamaAccount> Accounts { get; set; } = [];

    public string ActiveAccountIdentifier { get; set; } = "";
}