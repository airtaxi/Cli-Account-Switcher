namespace CliAccountSwitcher.Api.Providers.Ollama.Models;

public sealed class OllamaAuthExpiredException(string message) : Exception(message);