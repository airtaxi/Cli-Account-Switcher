using System.Collections.Generic;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class ZaiAccountStoreDocument
{
    public int Version { get; set; } = 1;

    public List<ZaiAccount> Accounts { get; set; } = [];

    public string ActiveAccountIdentifier { get; set; } = "";
}
