using CliAccountSwitcher.Api.Providers.Codex.Models.Authentication;
using CliAccountSwitcher.Api.Providers.Codex.Models.Usage;
using CliAccountSwitcher.Api.Providers.Ollama.Models.Usage;
using CliAccountSwitcher.Api.Providers.OpenCodeGo.Models.Usage;
using CliAccountSwitcher.Api.Providers.Zai.Models.Usage;
using CliAccountSwitcher.WinUI.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CliAccountSwitcher.WinUI.Helpers;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CodexAccountStoreDocument))]
[JsonSerializable(typeof(List<CodexAccount>))]
[JsonSerializable(typeof(CodexAccount))]
[JsonSerializable(typeof(CodexAuthenticationDocument))]
[JsonSerializable(typeof(CodexAuthenticationTokenDocument))]
[JsonSerializable(typeof(CodexUsageSnapshot))]
[JsonSerializable(typeof(CodexUsageWindow))]
[JsonSerializable(typeof(ApplicationSettings))]
[JsonSerializable(typeof(ClaudeCodeBackupDocument))]
[JsonSerializable(typeof(ClaudeCodeBackupAccountDocument))]
[JsonSerializable(typeof(ZaiAccountStoreDocument))]
[JsonSerializable(typeof(List<ZaiAccount>))]
[JsonSerializable(typeof(ZaiAccount))]
[JsonSerializable(typeof(ZaiUsageSnapshot))]
[JsonSerializable(typeof(ZaiUsageWindow))]
[JsonSerializable(typeof(OpenCodeGoAccountStoreDocument))]
[JsonSerializable(typeof(List<OpenCodeGoAccount>))]
[JsonSerializable(typeof(OpenCodeGoAccount))]
[JsonSerializable(typeof(OpenCodeGoUsageSnapshot))]
[JsonSerializable(typeof(OpenCodeGoUsageWindow))]
[JsonSerializable(typeof(OllamaAccountStoreDocument))]
[JsonSerializable(typeof(List<OllamaAccount>))]
[JsonSerializable(typeof(OllamaAccount))]
[JsonSerializable(typeof(OllamaUsageSnapshot))]
[JsonSerializable(typeof(OllamaUsageWindow))]
public sealed partial class CodexAccountJsonSerializerContext : JsonSerializerContext;
