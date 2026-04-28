using System;
using System.Collections.Generic;

namespace CliAccountSwitcher.Api.Test.Infrastructure;

public sealed class CommandLineArguments
{
    private readonly Dictionary<string, string?> _optionValues;
    private readonly List<string> _positionalValues;

    private CommandLineArguments(Dictionary<string, string?> optionValues, List<string> positionalValues)
    {
        _optionValues = optionValues;
        _positionalValues = positionalValues;
    }

    public static CommandLineArguments Parse(IReadOnlyList<string> commandLineArguments)
    {
        var optionValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positionalValues = new List<string>();

        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Count; argumentIndex++)
        {
            var argumentText = commandLineArguments[argumentIndex];
            if (!argumentText.StartsWith("--", StringComparison.Ordinal))
            {
                positionalValues.Add(argumentText);
                continue;
            }

            var separatorIndex = argumentText.IndexOf('=');
            if (separatorIndex > 2)
            {
                optionValues[argumentText[2..separatorIndex]] = argumentText[(separatorIndex + 1)..];
                continue;
            }

            var optionName = argumentText[2..];
            if (argumentIndex + 1 < commandLineArguments.Count && !commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                optionValues[optionName] = commandLineArguments[argumentIndex + 1];
                argumentIndex++;
                continue;
            }

            optionValues[optionName] = null;
        }

        return new CommandLineArguments(optionValues, positionalValues);
    }

    public bool HasOption(string optionName) => _optionValues.ContainsKey(optionName);

    public string? GetOptionValue(string optionName) => _optionValues.TryGetValue(optionName, out var optionValue) ? optionValue : null;

    public string? GetPositionalValue(int index) => index >= 0 && index < _positionalValues.Count ? _positionalValues[index] : null;
}
