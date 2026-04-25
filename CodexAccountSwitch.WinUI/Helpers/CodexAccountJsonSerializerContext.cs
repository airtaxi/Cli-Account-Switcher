using CodexAccountSwitch.Api.Models.Authentication;
using CodexAccountSwitch.Api.Models.Usage;
using CodexAccountSwitch.WinUI.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodexAccountSwitch.WinUI.Helpers;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CodexAccountStoreDocument))]
[JsonSerializable(typeof(List<CodexAccount>))]
[JsonSerializable(typeof(CodexAccount))]
[JsonSerializable(typeof(CodexAuthenticationDocument))]
[JsonSerializable(typeof(CodexAuthenticationTokenDocument))]
[JsonSerializable(typeof(CodexUsageSnapshot))]
[JsonSerializable(typeof(CodexUsageWindow))]
public sealed partial class CodexAccountJsonSerializerContext : JsonSerializerContext
{
}
