using CliAccountSwitcher.Api.Providers.Abstractions;

namespace CliAccountSwitcher.WinUI.Models;

public sealed class ProviderAccountsChangedMessage(CliProviderKind providerKind)
{
    public CliProviderKind ProviderKind { get; } = providerKind;
}
