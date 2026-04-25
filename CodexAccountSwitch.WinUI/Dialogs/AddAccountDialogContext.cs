using CodexAccountSwitch.Api.Authentication;
using CodexAccountSwitch.WinUI.Services;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace CodexAccountSwitch.WinUI.Dialogs;

public sealed class AddAccountDialogContext(CodexAccountService codexAccountService, ContentDialog contentDialog)
{
    private CodexOAuthSession _codexOAuthSession;

    public CodexAccountService CodexAccountService { get; } = codexAccountService;

    public ContentDialog ContentDialog { get; } = contentDialog;

    public void SetInteractionEnabled(bool isInteractionEnabled) => ContentDialog.IsEnabled = isInteractionEnabled;

    public void CompleteSuccessfully() => ContentDialog.Hide();

    public async Task SetOAuthSessionAsync(CodexOAuthSession codexOAuthSession)
    {
        await DisposeOAuthSessionAsync();
        _codexOAuthSession = codexOAuthSession;
    }

    public async Task DisposeOAuthSessionAsync()
    {
        if (_codexOAuthSession is null) return;

        var codexOAuthSession = _codexOAuthSession;
        _codexOAuthSession = null;
        await codexOAuthSession.DisposeAsync();
    }
}
