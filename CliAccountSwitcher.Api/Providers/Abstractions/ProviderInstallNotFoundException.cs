namespace CliAccountSwitcher.Api.Providers.Abstractions;

public sealed class ProviderInstallNotFoundException(string message, Exception? innerException = null) : Exception(message, innerException);
