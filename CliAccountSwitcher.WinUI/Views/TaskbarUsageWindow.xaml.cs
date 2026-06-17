using CliAccountSwitcher.WinUI.Helpers;
using Deskband11Lib.Core;
using Deskband11Lib.WinUI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.Versioning;

namespace CliAccountSwitcher.WinUI.Views;

[SupportedOSPlatform("windows10.0.22000.0")]
public sealed partial class TaskbarUsageWindow : Window
{
    public TaskbarContentHost TaskbarContentHost { get; }

    public TaskbarUsageWindow()
    {
        InitializeComponent();

        TaskbarContentHost = new TaskbarContentHost(this, (FrameworkElement)Content, new() { PreferredWidth = TaskbarHelper.PreferredTaskbarContentWidth });
    }

    public async Task PrepareTaskbarContentAsync() => await TaskbarContentHost.AttachWhenLayoutReadyAsync();

    private void OnTaskbarUsageWindowClosed(object sender, WindowEventArgs e)
    {
        TaskbarContentHost.Dispose();
        TaskbarUsageContent.Dispose();
    }
}
