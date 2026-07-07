using Microsoft.UI.Xaml.Data;

namespace CliAccountSwitcher.WinUI.Models;

[Bindable]
public sealed class MonitorIdentityOption
{
    public int Identity { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
}