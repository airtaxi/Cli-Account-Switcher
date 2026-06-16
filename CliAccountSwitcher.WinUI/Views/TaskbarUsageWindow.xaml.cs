using Deskband11Lib;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace CliAccountSwitcher.WinUI.Views;

public sealed partial class TaskbarUsageWindow : Window
{
    public const double PreferredTaskbarContentWidth = 200;

    public TaskbarContentHost TaskbarContentHost { get; }

    public TaskbarUsageWindow()
    {
        InitializeComponent();

        TaskbarContentHost = new TaskbarContentHost(this, (FrameworkElement)Content, new() { PreferredWidth = PreferredTaskbarContentWidth });
    }

    public async Task PrepareTaskbarContentAsync() => await TaskbarContentHost.AttachWhenLayoutReadyAsync();

    private void OnTaskbarUsageWindowClosed(object sender, WindowEventArgs e)
    {
        TaskbarContentHost.Dispose();
        TaskbarUsageContent.Dispose();
    }
}
