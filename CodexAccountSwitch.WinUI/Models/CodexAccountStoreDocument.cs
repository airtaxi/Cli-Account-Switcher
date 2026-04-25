using System.Collections.Generic;

namespace CodexAccountSwitch.WinUI.Models;

public sealed class CodexAccountStoreDocument
{
    public int Version { get; set; } = 1;

    public List<CodexAccount> Accounts { get; set; } = [];
}
