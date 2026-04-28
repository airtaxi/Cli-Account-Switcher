using CliAccountSwitcher.Api.Authentication;
using CliAccountSwitcher.Api.Infrastructure;
using CliAccountSwitcher.Api.Infrastructure.Http;
using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Models.Authentication;
using CliAccountSwitcher.Api.Models.Responses;
using CliAccountSwitcher.Api.Test.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CliAccountSwitcher.Api.Test;

public sealed class CodexApiExperimentApplication : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CodexApiClientOptions _codexApiClientOptions;
    private readonly CodexAuthenticationDocumentSerializer _codexAuthenticationDocumentSerializer;
    private readonly CodexOAuthClient _codexOAuthClient;
    private readonly CodexUsageClient _codexUsageClient;
    private readonly CodexModelsClient _codexModelsClient;
    private readonly CodexResponsesClient _codexResponsesClient;

    public CodexApiExperimentApplication()
    {
        _httpClient = CodexHttpClientFactory.CreateDefault();
        _codexApiClientOptions = new CodexApiClientOptions();
        _codexAuthenticationDocumentSerializer = new CodexAuthenticationDocumentSerializer();

        var codexClientMetadataProvider = new CodexClientMetadataProvider(_codexApiClientOptions);
        var codexRequestMessageFactory = new CodexRequestMessageFactory(_codexApiClientOptions, codexClientMetadataProvider);

        _codexOAuthClient = new CodexOAuthClient(_httpClient, _codexApiClientOptions, codexRequestMessageFactory);
        _codexUsageClient = new CodexUsageClient(_httpClient, codexRequestMessageFactory);
        _codexModelsClient = new CodexModelsClient(_httpClient, codexRequestMessageFactory);
        _codexResponsesClient = new CodexResponsesClient(_httpClient, codexRequestMessageFactory);
    }

    public async Task<int> RunAsync(string[] commandLineArguments)
    {
        try
        {
            if (commandLineArguments.Length == 0) return await RunInteractiveLoopAsync();

            var commandName = commandLineArguments[0];
            var parsedArguments = CommandLineArguments.Parse(commandLineArguments.Length <= 1 ? [] : commandLineArguments[1..]);
            return await RunCommandAsync(commandName, parsedArguments);
        }
        catch (CodexApiException exception)
        {
            Console.Error.WriteLine(exception.Message);
            if (!string.IsNullOrWhiteSpace(exception.ResponseBody))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Response body:");
                Console.Error.WriteLine(exception.ResponseBody);
            }

            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private int ShowHelp()
    {
        Console.WriteLine("CliAccountSwitcher.Api.Test");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help");
        Console.WriteLine("  normalize-auth [--input <path>] [--output <path>]");
        Console.WriteLine("  show-identity [--input <path>]");
        Console.WriteLine("  oauth-login [--output <path>]");
        Console.WriteLine("  usage [--input <path>]");
        Console.WriteLine("  models [--input <path>]");
        Console.WriteLine("  response --model <model> --text <text> [--input <path>] [--raw]");
        Console.WriteLine("  stream-response --model <model> --text <text> [--input <path>]");
        Console.WriteLine();
        Console.WriteLine("Run without arguments to start the interactive command loop.");
        Console.WriteLine("If --input is omitted, ~/.codex/auth.json is used.");
        return 0;
    }

    private int ShowUnknownCommand(string commandName)
    {
        Console.Error.WriteLine($"Unknown command: {commandName}");
        Console.Error.WriteLine("Run `help` to see the available commands.");
        return 1;
    }

    private async Task<int> RunCommandAsync(string commandName, CommandLineArguments parsedArguments)
        => commandName.ToLowerInvariant() switch
        {
            "help" => ShowHelp(),
            "normalize-auth" => await NormalizeAuthenticationDocumentAsync(parsedArguments),
            "show-identity" => await ShowIdentityAsync(parsedArguments),
            "oauth-login" => await RunOAuthLoginAsync(parsedArguments),
            "usage" => await ShowUsageAsync(parsedArguments),
            "models" => await ShowModelsAsync(parsedArguments),
            "response" => await CreateResponseAsync(parsedArguments),
            "stream-response" => await StreamResponseAsync(parsedArguments),
            _ => ShowUnknownCommand(commandName)
        };

    private async Task<int> RunInteractiveLoopAsync()
    {
        while (true)
        {
            ShowInteractiveMenu();

            var selectedCommandName = ResolveInteractiveCommandName(ReadRequiredValue("Select a command"));
            if (string.IsNullOrWhiteSpace(selectedCommandName))
            {
                Console.WriteLine("Invalid selection.");
                Console.WriteLine();
                continue;
            }

            if (selectedCommandName == "exit") return 0;

            var parsedArguments = BuildInteractiveArguments(selectedCommandName);
            Console.WriteLine();

            var commandExitCode = await RunCommandAsync(selectedCommandName, parsedArguments);
            Console.WriteLine();
            Console.WriteLine(commandExitCode == 0 ? "Completed." : $"The command exited with code {commandExitCode}.");
            Console.WriteLine();
            WaitForContinue();
        }
    }

    private void ShowInteractiveMenu()
    {
        Console.WriteLine("CliAccountSwitcher.Api.Test");
        Console.WriteLine("Choose a feature:");
        Console.WriteLine("  1. Help");
        Console.WriteLine("  2. Normalize auth.json");
        Console.WriteLine("  3. Show identity");
        Console.WriteLine("  4. OAuth login");
        Console.WriteLine("  5. Usage");
        Console.WriteLine("  6. Models");
        Console.WriteLine("  7. Response");
        Console.WriteLine("  8. Stream response");
        Console.WriteLine("  0. Exit");
        Console.WriteLine();
    }

    private CommandLineArguments BuildInteractiveArguments(string commandName)
    {
        var interactiveArguments = new List<string>();
        var defaultAuthenticationFilePath = BuildDefaultAuthenticationFilePath();

        switch (commandName)
        {
            case "help":
                break;
            case "normalize-auth":
                AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultAuthenticationFilePath})"));
                AppendOptionalArgument(interactiveArguments, "output", ReadOptionalValue("Output file path (Enter to print to the console)"));
                break;
            case "show-identity":
            case "usage":
            case "models":
                AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultAuthenticationFilePath})"));
                break;
            case "oauth-login":
                AppendOptionalArgument(interactiveArguments, "output", ReadOptionalValue("Output file path (Enter to print to the console)"));
                break;
            case "response":
                AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultAuthenticationFilePath})"));
                AppendRequiredArgument(interactiveArguments, "model", ReadRequiredValue("Model"));
                AppendRequiredArgument(interactiveArguments, "text", ReadRequiredValue("Prompt text"));
                AppendFlagArgument(interactiveArguments, "raw", ReadBooleanValue("Show raw response? [y/N]", false));
                break;
            case "stream-response":
                AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultAuthenticationFilePath})"));
                AppendRequiredArgument(interactiveArguments, "model", ReadRequiredValue("Model"));
                AppendRequiredArgument(interactiveArguments, "text", ReadRequiredValue("Prompt text"));
                break;
        }

        return CommandLineArguments.Parse(interactiveArguments);
    }

    private static string? ResolveInteractiveCommandName(string selectionText)
        => selectionText.Trim().ToLowerInvariant() switch
        {
            "0" or "q" or "quit" or "exit" => "exit",
            "1" or "help" => "help",
            "2" or "normalize-auth" => "normalize-auth",
            "3" or "show-identity" => "show-identity",
            "4" or "oauth-login" => "oauth-login",
            "5" or "usage" => "usage",
            "6" or "models" => "models",
            "7" or "response" => "response",
            "8" or "stream-response" => "stream-response",
            _ => null
        };

    private static void AppendOptionalArgument(List<string> interactiveArguments, string optionName, string? optionValue)
    {
        if (string.IsNullOrWhiteSpace(optionValue)) return;
        interactiveArguments.Add($"--{optionName}");
        interactiveArguments.Add(optionValue);
    }

    private static void AppendRequiredArgument(List<string> interactiveArguments, string optionName, string optionValue)
    {
        interactiveArguments.Add($"--{optionName}");
        interactiveArguments.Add(optionValue);
    }

    private static void AppendFlagArgument(List<string> interactiveArguments, string optionName, bool isEnabled)
    {
        if (!isEnabled) return;
        interactiveArguments.Add($"--{optionName}");
    }

    private static string ReadRequiredValue(string promptText)
    {
        while (true)
        {
            Console.Write($"{promptText}: ");
            var inputText = Console.ReadLine()?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(inputText)) return inputText;
            Console.WriteLine("A value is required.");
        }
    }

    private static string? ReadOptionalValue(string promptText)
    {
        Console.Write($"{promptText}: ");
        var inputText = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(inputText) ? null : inputText;
    }

    private static bool ReadBooleanValue(string promptText, bool defaultValue)
    {
        while (true)
        {
            Console.Write($"{promptText} ");
            var inputText = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(inputText)) return defaultValue;
            if (inputText is "y" or "yes" or "1" or "true") return true;
            if (inputText is "n" or "no" or "0" or "false") return false;
            Console.WriteLine("Enter y or n.");
        }
    }

    private static void WaitForContinue()
    {
        Console.Write("Press Enter to continue...");
        Console.ReadLine();
        Console.WriteLine();
    }

    private async Task<int> NormalizeAuthenticationDocumentAsync(CommandLineArguments parsedArguments)
    {
        var authenticationFilePath = ResolveAuthenticationFilePath(parsedArguments);
        var normalizedAuthenticationDocumentText = CodexAuthenticationDocumentSerializer.Normalize(await File.ReadAllTextAsync(authenticationFilePath));
        var outputFilePath = parsedArguments.GetOptionValue("output");
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            Console.WriteLine(normalizedAuthenticationDocumentText);
            return 0;
        }

        await File.WriteAllTextAsync(outputFilePath, normalizedAuthenticationDocumentText);
        Console.WriteLine($"Normalized authentication document written to: {outputFilePath}");
        return 0;
    }

    private async Task<int> ShowIdentityAsync(CommandLineArguments parsedArguments)
    {
        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(parsedArguments);
        var codexIdentityProfile = codexAuthenticationDocument.TryReadIdentityProfile();

        Console.WriteLine($"Authentication file: {ResolveAuthenticationFilePath(parsedArguments)}");
        Console.WriteLine($"Email: {codexIdentityProfile?.EmailAddress ?? codexAuthenticationDocument.EmailAddress}");
        Console.WriteLine($"Account identifier: {codexIdentityProfile?.AccountIdentifier ?? codexAuthenticationDocument.GetEffectiveAccountIdentifier()}");
        Console.WriteLine($"Plan type: {codexIdentityProfile?.PlanType ?? "(unknown)"}");
        Console.WriteLine($"Access token preview: {BuildAccessTokenPreview(codexAuthenticationDocument.GetEffectiveAccessToken())}");
        Console.WriteLine($"Expiration: {codexAuthenticationDocument.ExpirationText}");
        return 0;
    }

    private async Task<int> RunOAuthLoginAsync(CommandLineArguments parsedArguments)
    {
        await using var codexOAuthSession = _codexOAuthClient.CreateSession();

        Console.WriteLine("Open the following URL in a browser and complete the authorization flow:");
        Console.WriteLine(codexOAuthSession.AuthorizationAddress);
        if (TryOpenBrowser(codexOAuthSession.AuthorizationAddress)) Console.WriteLine("The system browser has been opened automatically.");
        else Console.WriteLine("The system browser could not be opened automatically. Open the URL manually.");
        Console.WriteLine($"Listening for the OAuth callback on: {codexOAuthSession.RedirectAddress}");
        Console.WriteLine();
        Console.WriteLine("Waiting for the loopback callback...");

        var codexOAuthCallbackPayload = await codexOAuthSession.WaitForCallbackAsync();

        var codexOAuthTokenExchangeResult = await _codexOAuthClient.ExchangeAuthorizationCodeAsync(codexOAuthSession, codexOAuthCallbackPayload);
        var codexAuthenticationDocument = CodexOAuthClient.CreateAuthenticationDocument(codexOAuthTokenExchangeResult);
        var serializedAuthenticationDocument = _codexAuthenticationDocumentSerializer.Serialize(codexAuthenticationDocument);
        var outputFilePath = parsedArguments.GetOptionValue("output");

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            Console.WriteLine(serializedAuthenticationDocument);
            return 0;
        }

        await File.WriteAllTextAsync(outputFilePath, serializedAuthenticationDocument);
        Console.WriteLine($"Authentication document written to: {outputFilePath}");
        return 0;
    }

    private async Task<int> ShowUsageAsync(CommandLineArguments parsedArguments)
    {
        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(parsedArguments);
        var codexUsageSnapshot = await _codexUsageClient.GetUsageAsync(codexAuthenticationDocument);

        Console.WriteLine($"Plan type: {codexUsageSnapshot.PlanType}");
        Console.WriteLine($"Email: {codexUsageSnapshot.EmailAddress}");
        Console.WriteLine($"Primary window remaining: {FormatPercentage(codexUsageSnapshot.PrimaryWindow.RemainingPercentage)}");
        Console.WriteLine($"Primary window reset after seconds: {codexUsageSnapshot.PrimaryWindow.ResetAfterSeconds}");
        Console.WriteLine($"Secondary window remaining: {FormatPercentage(codexUsageSnapshot.SecondaryWindow.RemainingPercentage)}");
        Console.WriteLine($"Secondary window reset after seconds: {codexUsageSnapshot.SecondaryWindow.ResetAfterSeconds}");
        return 0;
    }

    private async Task<int> ShowModelsAsync(CommandLineArguments parsedArguments)
    {
        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(parsedArguments);
        var modelDefinitions = await _codexModelsClient.GetModelsAsync(codexAuthenticationDocument);
        foreach (var modelDefinition in modelDefinitions) Console.WriteLine($"{modelDefinition.Identifier} ({modelDefinition.SourcePath})");
        return 0;
    }

    private async Task<int> CreateResponseAsync(CommandLineArguments parsedArguments)
    {
        var modelIdentifier = parsedArguments.GetOptionValue("model");
        var inputText = parsedArguments.GetOptionValue("text");
        if (string.IsNullOrWhiteSpace(modelIdentifier) || string.IsNullOrWhiteSpace(inputText)) throw new InvalidOperationException("The response command requires both --model and --text.");

        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(parsedArguments);
        var codexResponseRequest = new CodexResponseRequest
        {
            Model = modelIdentifier,
            InputText = inputText,
            Store = false
        };

        var codexResponseResult = await _codexResponsesClient.CreateResponseAsync(codexAuthenticationDocument, codexResponseRequest, parsedArguments.HasOption("compact"));
        Console.WriteLine(codexResponseResult.OutputText);
        if (!parsedArguments.HasOption("raw")) return 0;

        Console.WriteLine();
        Console.WriteLine("Raw response:");
        Console.WriteLine(codexResponseResult.RawResponseText);
        return 0;
    }

    private async Task<int> StreamResponseAsync(CommandLineArguments parsedArguments)
    {
        var modelIdentifier = parsedArguments.GetOptionValue("model");
        var inputText = parsedArguments.GetOptionValue("text");
        if (string.IsNullOrWhiteSpace(modelIdentifier) || string.IsNullOrWhiteSpace(inputText)) throw new InvalidOperationException("The stream-response command requires both --model and --text.");

        var codexAuthenticationDocument = await LoadAuthenticationDocumentAsync(parsedArguments);
        var codexResponseRequest = new CodexResponseRequest
        {
            Model = modelIdentifier,
            InputText = inputText,
            Stream = true,
            Store = false
        };

        await foreach (var codexResponseStreamEvent in _codexResponsesClient.StreamResponseAsync(codexAuthenticationDocument, codexResponseRequest, parsedArguments.HasOption("compact")))
        {
            if (codexResponseStreamEvent.IsTerminal)
            {
                Console.WriteLine("[DONE]");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(codexResponseStreamEvent.EventName)) Console.WriteLine($"event: {codexResponseStreamEvent.EventName}");
            Console.WriteLine($"data: {codexResponseStreamEvent.Data}");
            Console.WriteLine();
        }

        return 0;
    }

    private async Task<CodexAuthenticationDocument> LoadAuthenticationDocumentAsync(CommandLineArguments parsedArguments)
    {
        var authenticationFilePath = ResolveAuthenticationFilePath(parsedArguments);
        var authenticationDocumentText = await File.ReadAllTextAsync(authenticationFilePath);
        return CodexAuthenticationDocumentSerializer.Parse(authenticationDocumentText);
    }

    private string ResolveAuthenticationFilePath(CommandLineArguments parsedArguments)
    {
        var optionAuthenticationFilePath = parsedArguments.GetOptionValue("input");
        if (!string.IsNullOrWhiteSpace(optionAuthenticationFilePath)) return Path.GetFullPath(optionAuthenticationFilePath);

        var defaultAuthenticationFilePath = BuildDefaultAuthenticationFilePath();
        if (File.Exists(defaultAuthenticationFilePath)) return defaultAuthenticationFilePath;

        throw new FileNotFoundException("The authentication document could not be found. Pass --input <path> to specify it explicitly.", defaultAuthenticationFilePath);
    }

    private string BuildDefaultAuthenticationFilePath() => Path.Combine(_codexApiClientOptions.CodexHomeDirectoryPath, "auth.json");

    private static bool TryOpenBrowser(Uri address)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = address.ToString(),
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildAccessTokenPreview(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return "(missing)";
        return accessToken.Length <= 18 ? accessToken : $"{accessToken[..8]}...{accessToken[^6..]}";
    }

    private static string FormatPercentage(int percentageValue) => percentageValue < 0 ? "(unknown)" : $"{percentageValue}%";
}
