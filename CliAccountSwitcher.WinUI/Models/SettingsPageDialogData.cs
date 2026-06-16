namespace CliAccountSwitcher.WinUI.Models;

public sealed record SettingsPageDialogData(string Title, string Message, string PrimaryButtonText = null, string SecondaryButtonText = null, bool ShouldNavigateToSettingsAfterClose = false, bool ShouldRefreshStartupLaunchStateAfterClose = false);
