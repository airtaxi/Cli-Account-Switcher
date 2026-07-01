namespace CliAccountSwitcher.Api.Providers.Ollama;

public static class OllamaApiConventions
{
    public static Uri SettingsBaseUri { get; } = new("https://ollama.com");

    public static string SettingsPath => "/settings";

    public static string SignInPath => "/signin";

    public static string AuthCookieName => "__Secure-session";

    public static string UserAgent => "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
}