using CommunityToolkit.Mvvm.ComponentModel;

namespace CliAccountSwitcher.WinUI.ViewModels;

public sealed partial class ManualAuthenticationInputViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string AuthenticationDocumentText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }
}
