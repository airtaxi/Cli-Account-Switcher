namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderActionRequiredException(string userMessage, Exception? innerException = null) : Exception(userMessage, innerException)
{
    public string UserMessage { get; } = userMessage;
}
