using System.Collections.Generic;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class CodexAccountStoreDocument
{
    public int Version { get; set; } = 1;

    public List<CodexAccount> Accounts { get; set; } = [];
}
