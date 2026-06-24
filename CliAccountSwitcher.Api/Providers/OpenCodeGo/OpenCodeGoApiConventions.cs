namespace CliAccountSwitcher.Api.Providers.OpenCodeGo;

public static class OpenCodeGoApiConventions
{
    public static Uri ConsoleBaseUri { get; } = new("https://opencode.ai");

    public static string AuthStatusPath => "/auth/status";

    public static string AuthAuthorizePath => "/auth/authorize";

    public static string UsagePagePathTemplate => "/workspace/{0}/go";

    public static string KeysPagePathTemplate => "/workspace/{0}/keys";

    public static string AuthCookieName => "auth";

    public static string ProviderId => "opencode-go";
}
