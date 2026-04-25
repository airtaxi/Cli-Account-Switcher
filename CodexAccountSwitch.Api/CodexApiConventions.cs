namespace CodexAccountSwitch.Api;

public static class CodexApiConventions
{
    public static Uri ChatGptBaseUri { get; } = new("https://chatgpt.com");

    public static Uri OAuthBaseUri { get; } = new("https://auth.openai.com");

    public static string UsagePath => "/backend-api/wham/usage";

    public static string ResponsesPath => "/backend-api/codex/responses";

    public static string CompactResponsesPath => "/backend-api/codex/responses/compact";

    public static string DefaultResponsesInstructionsText => "You are Codex, a helpful assistant.";

    public static string ModelsPath => "/backend-api/models";

    public static string CodexModelsPath => "/backend-api/codex/models";

    public static string ClientVersionQueryParameterName => "client_version";

    public static string OAuthAuthorizePath => "/oauth/authorize";

    public static string OAuthTokenPath => "/oauth/token";

    public static string ResponsesBetaHeaderValue => "responses=experimental";

    public static string CodexOriginator => "codex_cli_rs";

    public static string UsageOriginator => "codex_vscode";
}
