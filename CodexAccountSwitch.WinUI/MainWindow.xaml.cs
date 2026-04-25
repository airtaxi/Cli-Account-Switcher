using CodexAccountSwitch.WinUI.Services;
using CodexAccountSwitch.WinUI.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using WinRT.Interop;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace CodexAccountSwitch.WinUI;

public sealed partial class MainWindow : WindowEx
{
    public static MainWindow Instance { get; private set;  }

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        this.CenterOnScreen();
        AppFrame.Navigate(typeof(MainPage));

        RefreshLocalizedText();
        App.LocalizationService.LanguageChanged += RefreshLocalizedText;
    }

    public static void ShowLoading(string message = null)
    {
        Instance.DispatcherQueue.TryEnqueue(() =>
        {
            Instance.AppFrame.IsEnabled = false;

            if (string.IsNullOrWhiteSpace(message)) Instance.LoadingTextBlock.Visibility = Visibility.Collapsed;
            else
            {
                Instance.LoadingTextBlock.Visibility = Visibility.Visible;
                Instance.LoadingTextBlock.Text = message;
            }

            Instance.LoadingGrid.Visibility = Visibility.Visible;
        });
    }

    public static void HideLoading()
    {
        Instance.DispatcherQueue.TryEnqueue(() =>
        {
            Instance.LoadingGrid.Visibility = Visibility.Collapsed;
            Instance.AppFrame.IsEnabled = true;
        });
    }

    private void RefreshLocalizedText()
    {
        var localizedWindowTitle = App.LocalizationService.GetLocalizedString("MainWindow_AppTitleBar/Title");
        Title = localizedWindowTitle;
        AppTitleBar.Title = localizedWindowTitle;
    }

    private void OnAppFrameNavigated(object sender, NavigationEventArgs args)
    {
        var frame = sender as Frame;

        AppTitleBar.IsBackButtonVisible = frame?.CanGoBack == true;
    }

    private void OnAppTitleBarBackRequested(TitleBar sender, object args)
    {
        if (AppFrame.CanGoBack) AppFrame.GoBack();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.LocalizationService.LanguageChanged -= RefreshLocalizedText;
        App.CodexAccountService.Dispose();
    }
}
