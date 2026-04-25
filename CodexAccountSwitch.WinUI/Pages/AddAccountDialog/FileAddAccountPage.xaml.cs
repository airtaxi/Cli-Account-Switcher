using CodexAccountSwitch.WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CodexAccountSwitch.WinUI.Pages.AddAccountDialog;

public sealed partial class FileAddAccountPage : Page
{
    private AddAccountDialogContext _addAccountDialogContext;
    private bool _isCompletingSuccessfully;

    public FileAddAccountPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArguments)
    {
        _addAccountDialogContext = navigationEventArguments.Parameter as AddAccountDialogContext;
    }

    private async void OnImportAuthenticationFileButtonClicked(object sender, RoutedEventArgs routedEventArguments)
    {
        if (_addAccountDialogContext is null) return;

        var fileOpenPicker = CreateAuthenticationFileOpenPicker();
        var storageFile = await fileOpenPicker.PickSingleFileAsync();
        if (storageFile is null) return;

        FileErrorInfoBar.IsOpen = false;
        FileValidationProgressRing.IsActive = true;
        FileValidationProgressRing.Visibility = Visibility.Visible;
        _addAccountDialogContext.SetInteractionEnabled(false);

        try
        {
            var authenticationDocumentText = await FileIO.ReadTextAsync(storageFile);
            await _addAccountDialogContext.CodexAccountService.AddValidatedAuthenticationDocumentTextAsync(authenticationDocumentText);
            _isCompletingSuccessfully = true;
            _addAccountDialogContext.CompleteSuccessfully();
        }
        catch { FileErrorInfoBar.IsOpen = true; }
        finally
        {
            if (!_isCompletingSuccessfully) _addAccountDialogContext.SetInteractionEnabled(true);
            FileValidationProgressRing.IsActive = false;
            FileValidationProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private static FileOpenPicker CreateAuthenticationFileOpenPicker()
    {
        var fileOpenPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializeWithWindow.Initialize(fileOpenPicker, WindowNative.GetWindowHandle(MainWindow.Instance));
        fileOpenPicker.FileTypeFilter.Add(".json");
        return fileOpenPicker;
    }
}
