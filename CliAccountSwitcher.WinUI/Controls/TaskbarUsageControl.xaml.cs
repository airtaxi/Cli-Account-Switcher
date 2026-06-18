using CliAccountSwitcher.WinUI.Helpers;
using CliAccountSwitcher.WinUI.Views;
using CliAccountSwitcher.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CliAccountSwitcher.WinUI.Controls;

public sealed partial class TaskbarUsageControl : UserControl, IDisposable
{
    private const double RefreshButtonVisibilityWidthOffset = 26;

    private bool _disposed;

    public TaskbarUsageControlViewModel ViewModel { get; }

    public TaskbarUsageControl()
    {
        ViewModel = App.Services.GetRequiredService<TaskbarUsageControlViewModel>();

        InitializeComponent();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnTaskbarUsageControlLoaded(object sender, RoutedEventArgs e) => await ViewModel.ReloadUsageOrRefreshMissingActiveUsageAsync();

    private void OnRootButtonClicked(object sender, RoutedEventArgs e) => MainWindow.ShowActiveAccountQuotaPopup();

    private void OnButtonGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement rootElement) return;

        var shouldShowRefreshButton = rootElement.ActualWidth > TaskbarHelper.PreferredTaskbarContentWidth - RefreshButtonVisibilityWidthOffset;
        RefreshActiveAccountButton.Visibility = shouldShowRefreshButton ? Visibility.Visible : Visibility.Collapsed;
    }
}
