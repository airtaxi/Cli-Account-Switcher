using CliAccountSwitcher.Api.Models.Authentication;
using CliAccountSwitcher.Api.Models.Usage;
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
public sealed partial class CodexAccountJsonSerializerContext : JsonSerializerContext
{
}
