namespace CliAccountSwitcher.Api.Providers.Abstractions;

public class ProviderActionRequiredException(string userMessage, Exception? innerException = null) : Exception(userMessage, innerException)
{
    public string UserMessage { get; } = userMessage;
}

public sealed class ProviderAuthenticationExpiredException(string userMessage, Exception? innerException = null) : ProviderActionRequiredException(userMessage, innerException)
{
}
