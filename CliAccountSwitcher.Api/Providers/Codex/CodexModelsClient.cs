using System.Text.Json;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure;
using CliAccountSwitcher.Api.Providers.Codex.Infrastructure.Http;
using CliAccountSwitcher.Api.Providers.Codex.Models;
using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;

namespace CliAccountSwitcher.Api.Providers.Codex;

public sealed class CodexModelsClient(HttpClient httpClient, CodexRequestMessageFactory codexRequestMessageFactory)
{
    public Task<IReadOnlyList<CodexModelDefinition>> GetModelsAsync(CodexAuthenticationDocument codexAuthenticationDocument, CancellationToken cancellationToken = default) => GetModelsAsync(codexAuthenticationDocument.CreateRequestContext(), cancellationToken);

    public async Task<IReadOnlyList<CodexModelDefinition>> GetModelsAsync(CodexRequestContext codexRequestContext, CancellationToken cancellationToken = default)
    {
        using var httpRequestMessage = codexRequestMessageFactory.CreateCodexModelsRequest(codexRequestContext);
        using var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
        var responseText = await CodexHttpResponseValidator.EnsureSuccessAndReadContentAsync(httpResponseMessage, cancellationToken);
        var modelDefinitions = ParseModelDefinitions(responseText, CodexApiConventions.CodexModelsPath);
        if (modelDefinitions.Count > 0) return modelDefinitions;
        throw new CodexApiException("The Codex models response does not contain any model definitions.", httpResponseMessage.StatusCode, responseText);
    }

    private static List<CodexModelDefinition> ParseModelDefinitions(string responseText, string requestPath)
    {
        using var jsonDocument = JsonDocument.Parse(responseText);
        var modelDefinitions = new List<CodexModelDefinition>();
        var discoveredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectModelDefinitions(jsonDocument.RootElement, requestPath, modelDefinitions, discoveredIdentifiers);
        return modelDefinitions;
    }

    private static void CollectModelDefinitions(JsonElement jsonElement, string requestPath, List<CodexModelDefinition> modelDefinitions, HashSet<string> discoveredIdentifiers)
    {
        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in jsonElement.EnumerateObject())
                {
                    if ((string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) || string.Equals(property.Name, "slug", StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var identifier = property.Value.GetString() ?? "";
                        if (!LooksLikeModelIdentifier(identifier) || !discoveredIdentifiers.Add(identifier)) continue;

                        modelDefinitions.Add(new CodexModelDefinition
                        {
                            Identifier = identifier,
                            SourcePath = requestPath
                        });
                    }

                    CollectModelDefinitions(property.Value, requestPath, modelDefinitions, discoveredIdentifiers);
                }
                return;
            case JsonValueKind.Array:
                foreach (var arrayItem in jsonElement.EnumerateArray()) CollectModelDefinitions(arrayItem, requestPath, modelDefinitions, discoveredIdentifiers);
                return;
        }
    }

    private static bool LooksLikeModelIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length is < 2 or > 96) return false;

        var containsAlphabeticCharacter = false;
        foreach (var character in identifier)
        {
            if (char.IsLetter(character)) containsAlphabeticCharacter = true;
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.') continue;
            return false;
        }

        return containsAlphabeticCharacter;
    }
}
