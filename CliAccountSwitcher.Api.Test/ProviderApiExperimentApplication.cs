using CliAccountSwitcher.Api.Models;
using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.Api.Providers.Claude;
using CliAccountSwitcher.Api.Providers.Codex;
using CliAccountSwitcher.Api.Storage;
using CliAccountSwitcher.Api.Test.Infrastructure;

namespace CliAccountSwitcher.Api.Test;

public sealed class ProviderApiExperimentApplication : IDisposable
{
    private readonly IProviderAdapter _codexProviderAdapter;
    private readonly IProviderAdapter _claudeCodeProviderAdapter;
    private readonly IProviderSnapshotStore _providerSnapshotStore;
    private readonly IReadOnlyDictionary<CliProviderKind, IProviderAdapter> _providerAdapters;

    public ProviderApiExperimentApplication()
    {
        _providerSnapshotStore = new FileSystemProviderSnapshotStore();
        _codexProviderAdapter = new CodexProviderAdapter(_providerSnapshotStore);
        _claudeCodeProviderAdapter = new ClaudeCodeProviderAdapter(_providerSnapshotStore);
        _providerAdapters = new Dictionary<CliProviderKind, IProviderAdapter>
        {
            [CliProviderKind.Codex] = _codexProviderAdapter,
            [CliProviderKind.ClaudeCode] = _claudeCodeProviderAdapter
        };
    }

    public async Task<int> RunAsync(string[] commandLineArguments)
    {
        try
        {
            if (commandLineArguments.Length == 0) return await RunProviderSelectionLoopAsync();

            var directCommandLineContext = ParseDirectCommandLine(commandLineArguments);
            var providerAdapter = GetProviderAdapter(directCommandLineContext.ProviderKind);
            if (string.IsNullOrWhiteSpace(directCommandLineContext.CommandName)) return ShowHelp(providerAdapter);

            var parsedArguments = CommandLineArguments.Parse(directCommandLineContext.CommandArguments);
            return await RunCommandAsync(directCommandLineContext.CommandName, providerAdapter, parsedArguments);
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
        catch (ProviderActionRequiredException exception)
        {
            Console.Error.WriteLine(exception.UserMessage);
            return 1;
        }
        catch (ProviderInstallNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (NotSupportedException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public void Dispose()
    {
        if (_codexProviderAdapter is IDisposable codexProviderAdapter) codexProviderAdapter.Dispose();
        if (_claudeCodeProviderAdapter is IDisposable claudeCodeProviderAdapter) claudeCodeProviderAdapter.Dispose();
    }

    private async Task<int> RunProviderSelectionLoopAsync()
    {
        while (true)
        {
            ShowProviderSelectionMenu();
            var selectionText = ReadRequiredValue("Select a provider");
            if (!TryResolveProviderSelection(selectionText, out var providerKind, out var shouldExit))
            {
                Console.WriteLine("Invalid selection.");
                Console.WriteLine();
                continue;
            }

            if (shouldExit) return 0;

            var providerAdapter = GetProviderAdapter(providerKind);
            var interactiveLoopResult = await RunProviderInteractiveLoopAsync(providerAdapter);
            if (interactiveLoopResult == InteractiveLoopResult.ExitApplication) return 0;
        }
    }

    private async Task<InteractiveLoopResult> RunProviderInteractiveLoopAsync(IProviderAdapter providerAdapter)
    {
        while (true)
        {
            ShowInteractiveMenu(providerAdapter);

            var selectedCommandName = ResolveInteractiveCommandName(ReadRequiredValue("Select a command"));
            if (string.IsNullOrWhiteSpace(selectedCommandName))
            {
                Console.WriteLine("Invalid selection.");
                Console.WriteLine();
                continue;
            }

            if (selectedCommandName == "exit") return InteractiveLoopResult.ExitApplication;
            if (selectedCommandName == "change-provider") return InteractiveLoopResult.ChangeProvider;

            if (!IsCommandSupported(selectedCommandName, providerAdapter, null))
            {
                Console.WriteLine(GetUnsupportedCommandMessage(selectedCommandName, providerAdapter));
                Console.WriteLine();
                WaitForContinue();
                continue;
            }

            var parsedArguments = BuildInteractiveArguments(selectedCommandName, providerAdapter);
            Console.WriteLine();

            var commandExitCode = await RunCommandAsync(selectedCommandName, providerAdapter, parsedArguments);
            Console.WriteLine();
            Console.WriteLine(commandExitCode == 0 ? "Completed." : $"The command exited with code {commandExitCode}.");
            Console.WriteLine();
            WaitForContinue();
        }
    }

    private async Task<int> RunCommandAsync(string commandName, IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var normalizedCommandName = commandName.ToLowerInvariant();
        if (normalizedCommandName == "help") return ShowHelp(providerAdapter);
        if (!IsCommandSupported(normalizedCommandName, providerAdapter, parsedArguments))
        {
            Console.Error.WriteLine(GetUnsupportedCommandMessage(normalizedCommandName, providerAdapter));
            return 2;
        }

        var codexProviderAdapter = providerAdapter as CodexProviderAdapter;
        if (codexProviderAdapter is not null) codexProviderAdapter.InputFilePathOverride = parsedArguments.GetOptionValue("input");

        try
        {
            return normalizedCommandName switch
            {
                "normalize-auth" => await NormalizeAuthenticationDocumentAsync(providerAdapter, parsedArguments),
                "show-identity" => await ShowIdentityAsync(providerAdapter),
                "oauth-login" => await RunOAuthLoginAsync(providerAdapter, parsedArguments),
                "usage" => await ShowUsageAsync(providerAdapter, parsedArguments),
                "models" => await ShowModelsAsync(providerAdapter),
                "response" => await CreateResponseAsync(providerAdapter, parsedArguments),
                "stream-response" => await StreamResponseAsync(providerAdapter, parsedArguments),
                "list-saved-accounts" => await ListSavedAccountsAsync(providerAdapter),
                "save-current-account" => await SaveCurrentAccountAsync(providerAdapter),
                "activate-saved-account" => await ActivateSavedAccountAsync(providerAdapter, parsedArguments),
                "delete-saved-account" => await DeleteSavedAccountAsync(providerAdapter, parsedArguments),
                _ => ShowUnknownCommand(normalizedCommandName)
            };
        }
        finally
        {
            if (codexProviderAdapter is not null) codexProviderAdapter.InputFilePathOverride = null;
        }
    }

    private int ShowHelp(IProviderAdapter providerAdapter)
    {
        Console.WriteLine("CliAccountSwitcher.Api.Test");
        Console.WriteLine($"Provider: {providerAdapter.DisplayName}");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help");
        Console.WriteLine("  normalize-auth [--input <path>] [--output <path>]");
        Console.WriteLine("  show-identity [--input <path>]");
        Console.WriteLine("  oauth-login [--output <path>]");
        Console.WriteLine("  usage [--input <path>] [--slot <slot>]");
        Console.WriteLine("  models [--input <path>]");
        Console.WriteLine("  response --model <model> --text <text> [--instructions <text>] [--input <path>] [--raw]");
        Console.WriteLine("  stream-response --model <model> --text <text> [--instructions <text>] [--input <path>]");
        Console.WriteLine("  list-saved-accounts");
        Console.WriteLine("  save-current-account");
        Console.WriteLine("  activate-saved-account --slot <slot>");
        Console.WriteLine("  delete-saved-account --slot <slot>");
        Console.WriteLine();
        Console.WriteLine("Global options:");
        Console.WriteLine("  --provider <codex|claude>");
        Console.WriteLine();

        if (providerAdapter.ProviderKind == CliProviderKind.Codex)
        {
            Console.WriteLine("If --input is omitted, ~/.codex/auth.json is used.");
        }
        else
        {
            Console.WriteLine("Claude Code notes:");
            Console.WriteLine("  normalize-auth: unsupported");
            Console.WriteLine("  models: unsupported");
            Console.WriteLine("  oauth-login: runs `claude auth login`, then run save-current-account");
            Console.WriteLine("  usage: current live account or saved --slot");
            Console.WriteLine("  response: active live Claude Code account");
        }

        return 0;
    }

    private int ShowUnknownCommand(string commandName)
    {
        Console.Error.WriteLine($"Unknown command: {commandName}");
        Console.Error.WriteLine("Run `help` to see the available commands.");
        return 1;
    }

    private async Task<int> NormalizeAuthenticationDocumentAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var authenticationFilePath = parsedArguments.GetOptionValue("input") ?? providerAdapter.GetDefaultInputFilePath() ?? throw new InvalidOperationException("The authentication file path could not be resolved.");
        var normalizedAuthenticationDocumentText = await providerAdapter.NormalizeAuthenticationDocumentAsync(await File.ReadAllTextAsync(authenticationFilePath));
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

    private async Task<int> ShowIdentityAsync(IProviderAdapter providerAdapter)
    {
        var providerIdentityProfile = await providerAdapter.GetCurrentIdentityAsync();

        Console.WriteLine($"Provider: {providerAdapter.DisplayName}");
        Console.WriteLine($"Email: {FormatValue(providerIdentityProfile.EmailAddress)}");
        Console.WriteLine($"Display name: {FormatValue(providerIdentityProfile.DisplayName)}");
        Console.WriteLine($"Account identifier: {FormatValue(providerIdentityProfile.AccountIdentifier)}");
        Console.WriteLine($"Organization identifier: {FormatValue(providerIdentityProfile.OrganizationIdentifier)}");
        Console.WriteLine($"Organization name: {FormatValue(providerIdentityProfile.OrganizationName)}");
        Console.WriteLine($"Plan type: {FormatValue(providerIdentityProfile.PlanType)}");
        Console.WriteLine($"Access token preview: {FormatValue(providerIdentityProfile.AccessTokenPreview)}");
        Console.WriteLine($"Expiration: {FormatValue(providerIdentityProfile.ExpirationText)}");
        Console.WriteLine($"Logged in: {providerIdentityProfile.IsLoggedIn}");
        return 0;
    }

    private async Task<int> RunOAuthLoginAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var providerLoginResult = await providerAdapter.RunLoginAsync();
        var outputFilePath = parsedArguments.GetOptionValue("output");

        if (providerLoginResult.IsAuthenticationDocument && !string.IsNullOrWhiteSpace(outputFilePath))
        {
            await File.WriteAllTextAsync(outputFilePath, providerLoginResult.OutputText);
            Console.WriteLine($"Authentication document written to: {outputFilePath}");
        }
        else if (!string.IsNullOrWhiteSpace(providerLoginResult.OutputText))
        {
            Console.WriteLine(providerLoginResult.OutputText);
        }

        if (!string.IsNullOrWhiteSpace(providerLoginResult.CompletionMessage)) Console.WriteLine(providerLoginResult.CompletionMessage);
        if (providerLoginResult.ShouldPromptSaveCurrentAccount) Console.WriteLine("Run save-current-account after login completes.");
        return 0;
    }

    private async Task<int> ShowUsageAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var storedAccountIdentifier = parsedArguments.GetOptionValue("slot") ?? parsedArguments.GetPositionalValue(0);
        var providerUsageSnapshot = await providerAdapter.GetUsageAsync(storedAccountIdentifier);

        Console.WriteLine($"Provider: {providerAdapter.DisplayName}");
        Console.WriteLine($"Plan type: {FormatValue(providerUsageSnapshot.PlanType)}");
        Console.WriteLine($"Email: {FormatValue(providerUsageSnapshot.EmailAddress)}");
        ShowUsageWindow("Five-hour window", providerUsageSnapshot.FiveHour);
        ShowUsageWindow("Seven-day window", providerUsageSnapshot.SevenDay);
        return 0;
    }

    private async Task<int> ShowModelsAsync(IProviderAdapter providerAdapter)
    {
        var modelDefinitions = await providerAdapter.GetModelsAsync();
        foreach (var modelDefinition in modelDefinitions) Console.WriteLine(modelDefinition);
        return 0;
    }

    private async Task<int> CreateResponseAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var providerResponseRequest = CreateProviderResponseRequest(parsedArguments, "response");
        var providerResponseResult = await providerAdapter.CreateResponseAsync(providerResponseRequest);
        Console.WriteLine(providerResponseResult.OutputText);
        if (!parsedArguments.HasOption("raw")) return 0;

        Console.WriteLine();
        Console.WriteLine("Raw response:");
        Console.WriteLine(providerResponseResult.RawResponseText);
        return 0;
    }

    private async Task<int> StreamResponseAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var providerResponseRequest = CreateProviderResponseRequest(parsedArguments, "stream-response");
        await foreach (var providerResponseStreamEvent in providerAdapter.StreamResponseAsync(providerResponseRequest))
        {
            if (providerResponseStreamEvent.IsTerminal)
            {
                Console.WriteLine("[DONE]");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(providerResponseStreamEvent.EventName)) Console.WriteLine($"event: {providerResponseStreamEvent.EventName}");
            Console.WriteLine($"data: {providerResponseStreamEvent.Data}");
            Console.WriteLine();
        }

        return 0;
    }

    private async Task<int> ListSavedAccountsAsync(IProviderAdapter providerAdapter)
    {
        var storedProviderAccounts = await providerAdapter.ListStoredAccountsAsync(_providerSnapshotStore);
        if (storedProviderAccounts.Count == 0)
        {
            Console.WriteLine("No saved accounts.");
            return 0;
        }

        Console.WriteLine("Slot | Email | Organization | Active | Last updated");
        foreach (var storedProviderAccount in storedProviderAccounts)
        {
            Console.WriteLine($"{storedProviderAccount.SlotNumber} | {FormatValue(storedProviderAccount.EmailAddress)} | {FormatValue(storedProviderAccount.OrganizationName)} | {storedProviderAccount.IsActive} | {storedProviderAccount.LastUpdated:yyyy-MM-dd HH:mm:ss} UTC");
        }

        return 0;
    }

    private async Task<int> SaveCurrentAccountAsync(IProviderAdapter providerAdapter)
    {
        var storedProviderAccount = await providerAdapter.SaveCurrentAccountAsync(_providerSnapshotStore);
        Console.WriteLine($"Saved account slot {storedProviderAccount.SlotNumber}: {FormatValue(storedProviderAccount.EmailAddress)}");
        return 0;
    }

    private async Task<int> ActivateSavedAccountAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var storedAccountIdentifier = ResolveStoredAccountIdentifier(parsedArguments);
        var storedProviderAccount = await providerAdapter.ActivateStoredAccountAsync(_providerSnapshotStore, storedAccountIdentifier);
        Console.WriteLine($"Activated account slot {storedProviderAccount.SlotNumber}: {FormatValue(storedProviderAccount.EmailAddress)}");
        if (providerAdapter.ProviderKind == CliProviderKind.ClaudeCode) Console.WriteLine("Restart Claude Code or reopen the VS Code Claude session to pick up the new account state.");
        return 0;
    }

    private async Task<int> DeleteSavedAccountAsync(IProviderAdapter providerAdapter, CommandLineArguments parsedArguments)
    {
        var storedAccountIdentifier = ResolveStoredAccountIdentifier(parsedArguments);
        await providerAdapter.DeleteStoredAccountAsync(_providerSnapshotStore, storedAccountIdentifier);
        Console.WriteLine($"Deleted account slot {storedAccountIdentifier}.");
        return 0;
    }

    private CommandLineArguments BuildInteractiveArguments(string commandName, IProviderAdapter providerAdapter)
    {
        var interactiveArguments = new List<string>();
        var defaultInputFilePath = providerAdapter.GetDefaultInputFilePath();

        switch (commandName)
        {
            case "help":
            case "list-saved-accounts":
            case "save-current-account":
                break;
            case "normalize-auth":
                AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultInputFilePath})"));
                AppendOptionalArgument(interactiveArguments, "output", ReadOptionalValue("Output file path (Enter to print to the console)"));
                break;
            case "show-identity":
            case "models":
                AppendCodexInputArgument(interactiveArguments, providerAdapter, defaultInputFilePath);
                break;
            case "usage":
                if (providerAdapter.ProviderKind == CliProviderKind.ClaudeCode) AppendOptionalArgument(interactiveArguments, "slot", ReadOptionalValue("Stored account slot (Enter for current live account)"));
                else AppendCodexInputArgument(interactiveArguments, providerAdapter, defaultInputFilePath);
                break;
            case "oauth-login":
                if (providerAdapter.ProviderKind == CliProviderKind.Codex) AppendOptionalArgument(interactiveArguments, "output", ReadOptionalValue("Output file path (Enter to print to the console)"));
                break;
            case "response":
                AppendCodexInputArgument(interactiveArguments, providerAdapter, defaultInputFilePath);
                AppendRequiredArgument(interactiveArguments, "model", ReadRequiredValue("Model"));
                AppendRequiredArgument(interactiveArguments, "text", ReadRequiredValue("Prompt text"));
                AppendOptionalArgument(interactiveArguments, "instructions", ReadOptionalValue("Instructions (Enter to skip)"));
                AppendFlagArgument(interactiveArguments, "raw", ReadBooleanValue("Show raw response? [y/N]", false));
                break;
            case "stream-response":
                AppendCodexInputArgument(interactiveArguments, providerAdapter, defaultInputFilePath);
                AppendRequiredArgument(interactiveArguments, "model", ReadRequiredValue("Model"));
                AppendRequiredArgument(interactiveArguments, "text", ReadRequiredValue("Prompt text"));
                AppendOptionalArgument(interactiveArguments, "instructions", ReadOptionalValue("Instructions (Enter to skip)"));
                break;
            case "activate-saved-account":
            case "delete-saved-account":
                AppendRequiredArgument(interactiveArguments, "slot", ReadRequiredValue("Stored account slot"));
                break;
        }

        return CommandLineArguments.Parse(interactiveArguments);
    }

    private static bool IsCommandSupported(string commandName, IProviderAdapter providerAdapter, CommandLineArguments? parsedArguments)
        => commandName switch
        {
            "help" => true,
            "normalize-auth" => providerAdapter.Capabilities.SupportsAuthenticationDocumentNormalization,
            "show-identity" => true,
            "oauth-login" => providerAdapter.Capabilities.SupportsInteractiveLogin,
            "usage" => providerAdapter.Capabilities.SupportsUsage && (parsedArguments is null || string.IsNullOrWhiteSpace(parsedArguments.GetOptionValue("slot")) || providerAdapter.Capabilities.SupportsStoredAccountUsage),
            "models" => providerAdapter.Capabilities.SupportsModels,
            "response" => providerAdapter.Capabilities.SupportsResponses,
            "stream-response" => providerAdapter.Capabilities.SupportsStreamingResponses,
            "list-saved-accounts" or "save-current-account" or "activate-saved-account" or "delete-saved-account" => providerAdapter.Capabilities.SupportsSavedAccounts,
            _ => true
        };

    private static string GetUnsupportedCommandMessage(string commandName, IProviderAdapter providerAdapter)
        => providerAdapter.ProviderKind == CliProviderKind.ClaudeCode && commandName == "normalize-auth"
            ? "Claude Code는 raw auth 문서 정규화를 지원하지 않습니다. save-current-account 를 사용하세요."
            : $"{providerAdapter.DisplayName} does not support `{commandName}`.";

    private IProviderAdapter GetProviderAdapter(CliProviderKind providerKind) => _providerAdapters[providerKind];

    private static DirectCommandLineContext ParseDirectCommandLine(IReadOnlyList<string> commandLineArguments)
    {
        var providerKind = CliProviderKind.Codex;
        var commandName = "";
        var commandArguments = new List<string>();

        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Count; argumentIndex++)
        {
            var argumentText = commandLineArguments[argumentIndex];
            if (string.Equals(argumentText, "--provider", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentIndex + 1 >= commandLineArguments.Count) throw new InvalidOperationException("The --provider option requires a value.");
                providerKind = ParseProviderKind(commandLineArguments[argumentIndex + 1]);
                argumentIndex++;
                continue;
            }

            if (argumentText.StartsWith("--provider=", StringComparison.OrdinalIgnoreCase))
            {
                providerKind = ParseProviderKind(argumentText["--provider=".Length..]);
                continue;
            }

            if (string.IsNullOrWhiteSpace(commandName) && !argumentText.StartsWith("--", StringComparison.Ordinal))
            {
                commandName = argumentText;
                continue;
            }

            commandArguments.Add(argumentText);
        }

        return new DirectCommandLineContext
        {
            ProviderKind = providerKind,
            CommandName = commandName,
            CommandArguments = commandArguments
        };
    }

    private static CliProviderKind ParseProviderKind(string providerText)
        => providerText.Trim().ToLowerInvariant() switch
        {
            "1" or "codex" => CliProviderKind.Codex,
            "2" or "claude" or "claude-code" or "claudecode" => CliProviderKind.ClaudeCode,
            _ => throw new InvalidOperationException($"Unknown provider: {providerText}")
        };

    private static void ShowProviderSelectionMenu()
    {
        Console.WriteLine("CliAccountSwitcher.Api.Test");
        Console.WriteLine("Select a provider:");
        Console.WriteLine("  1. Codex");
        Console.WriteLine("  2. Claude Code");
        Console.WriteLine("  0. Exit");
        Console.WriteLine();
    }

    private static void ShowInteractiveMenu(IProviderAdapter providerAdapter)
    {
        Console.WriteLine("CliAccountSwitcher.Api.Test");
        Console.WriteLine($"Provider: {providerAdapter.DisplayName}");
        Console.WriteLine("Choose a feature:");
        Console.WriteLine("  1. Help");
        Console.WriteLine("  2. Normalize auth.json");
        Console.WriteLine("  3. Show identity");
        Console.WriteLine("  4. OAuth login");
        Console.WriteLine("  5. Usage");
        Console.WriteLine("  6. Models");
        Console.WriteLine("  7. Response");
        Console.WriteLine("  8. Stream response");
        Console.WriteLine("  9. List saved accounts");
        Console.WriteLine("  10. Save current account");
        Console.WriteLine("  11. Activate saved account");
        Console.WriteLine("  12. Delete saved account");
        Console.WriteLine("  13. Change provider");
        Console.WriteLine("  14. Exit");
        Console.WriteLine();
    }

    private static bool TryResolveProviderSelection(string selectionText, out CliProviderKind providerKind, out bool shouldExit)
    {
        providerKind = CliProviderKind.Codex;
        shouldExit = false;

        var normalizedSelectionText = selectionText.Trim().ToLowerInvariant();
        if (normalizedSelectionText is "0" or "q" or "quit" or "exit")
        {
            shouldExit = true;
            return true;
        }

        if (normalizedSelectionText is "1" or "codex")
        {
            providerKind = CliProviderKind.Codex;
            return true;
        }

        if (normalizedSelectionText is "2" or "claude" or "claude-code" or "claudecode")
        {
            providerKind = CliProviderKind.ClaudeCode;
            return true;
        }

        return false;
    }

    private static string? ResolveInteractiveCommandName(string selectionText)
        => selectionText.Trim().ToLowerInvariant() switch
        {
            "1" or "help" => "help",
            "2" or "normalize-auth" => "normalize-auth",
            "3" or "show-identity" => "show-identity",
            "4" or "oauth-login" => "oauth-login",
            "5" or "usage" => "usage",
            "6" or "models" => "models",
            "7" or "response" => "response",
            "8" or "stream-response" => "stream-response",
            "9" or "list-saved-accounts" => "list-saved-accounts",
            "10" or "save-current-account" => "save-current-account",
            "11" or "activate-saved-account" => "activate-saved-account",
            "12" or "delete-saved-account" => "delete-saved-account",
            "13" or "change-provider" => "change-provider",
            "14" or "0" or "q" or "quit" or "exit" => "exit",
            _ => null
        };

    private static ProviderResponseRequest CreateProviderResponseRequest(CommandLineArguments parsedArguments, string commandName)
    {
        var modelIdentifier = parsedArguments.GetOptionValue("model");
        var inputText = parsedArguments.GetOptionValue("text");
        if (string.IsNullOrWhiteSpace(modelIdentifier) || string.IsNullOrWhiteSpace(inputText)) throw new InvalidOperationException($"The {commandName} command requires both --model and --text.");

        return new ProviderResponseRequest
        {
            Model = modelIdentifier,
            Text = inputText,
            Instructions = parsedArguments.GetOptionValue("instructions")
        };
    }

    private static string ResolveStoredAccountIdentifier(CommandLineArguments parsedArguments)
    {
        var optionStoredAccountIdentifier = parsedArguments.GetOptionValue("slot");
        if (!string.IsNullOrWhiteSpace(optionStoredAccountIdentifier)) return optionStoredAccountIdentifier;

        var positionalStoredAccountIdentifier = parsedArguments.GetPositionalValue(0);
        if (!string.IsNullOrWhiteSpace(positionalStoredAccountIdentifier)) return positionalStoredAccountIdentifier;

        throw new InvalidOperationException("This command requires --slot <slot>.");
    }

    private static void ShowUsageWindow(string titleText, ProviderUsageWindow providerUsageWindow)
    {
        Console.WriteLine($"{titleText} used: {FormatPercentage(providerUsageWindow.UsedPercentage)}");
        Console.WriteLine($"{titleText} remaining: {FormatPercentage(providerUsageWindow.RemainingPercentage)}");
        Console.WriteLine($"{titleText} reset after seconds: {providerUsageWindow.ResetAfterSeconds}");
        Console.WriteLine($"{titleText} reset at: {FormatValue(providerUsageWindow.ResetAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
    }

    private static void AppendCodexInputArgument(List<string> interactiveArguments, IProviderAdapter providerAdapter, string? defaultInputFilePath)
    {
        if (providerAdapter.ProviderKind != CliProviderKind.Codex) return;
        AppendOptionalArgument(interactiveArguments, "input", ReadOptionalValue($"Authentication file path (Enter for default: {defaultInputFilePath})"));
    }

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

    private static string FormatPercentage(int percentageValue) => percentageValue < 0 ? "(unknown)" : $"{percentageValue}%";

    private static string FormatValue(string? valueText) => string.IsNullOrWhiteSpace(valueText) ? "(unknown)" : valueText;

    private enum InteractiveLoopResult
    {
        ChangeProvider,
        ExitApplication
    }

    private sealed class DirectCommandLineContext
    {
        public CliProviderKind ProviderKind { get; set; }

        public string CommandName { get; set; } = "";

        public IReadOnlyList<string> CommandArguments { get; set; } = [];
    }
}
