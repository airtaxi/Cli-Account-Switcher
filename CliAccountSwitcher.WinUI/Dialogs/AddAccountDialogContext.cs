using CliAccountSwitcher.Api.Authentication;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Services;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace CliAccountSwitcher.WinUI.Dialogs;

public sealed class AddAccountDialogContext(CodexAccountService codexAccountService, ContentDialog contentDialog)
{
    private CodexOAuthSession _codexOAuthSession;

    public CodexAccountService CodexAccountService { get; } = codexAccountService;

    public ContentDialog ContentDialog { get; } = contentDialog;

    public CliProviderKind SelectedProviderKind => App.ApplicationSettings.SelectedProviderKind;

    public void SetInteractionEnabled(bool isInteractionEnabled) => ContentDialog.IsEnabled = isInteractionEnabled;

    public void CompleteSuccessfully() => ContentDialog.Hide();

    public async Task RunClaudeCodeLoginAsync() => await App.CliProviderAccountService.RunClaudeCodeLoginAsync();

    public async Task SaveCurrentClaudeCodeAccountAsync() => await App.CliProviderAccountService.SaveCurrentClaudeCodeAccountAsync();

    public async Task SaveClaudeCodeAccountAsync(string credentialsJson, string globalConfigJson) => await App.CliProviderAccountService.SaveClaudeCodeAccountAsync(credentialsJson, globalConfigJson);

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
