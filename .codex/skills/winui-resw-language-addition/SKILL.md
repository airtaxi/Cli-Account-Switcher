---
name: winui-resw-language-addition
description: Add or update language support in a WinUI 3 app that uses .resw localization, ApplicationLanguages.PrimaryLanguageOverride, LocalizationService, ApplicationSettingsService language normalization, and a SettingsPage language ComboBox. Use when adding languages such as ja-JP, zh-Hans, zh-Hant, or other BCP-47 tags to this repository or a similarly structured WinUI RESW project.
---

# WinUI RESW Language Addition

## Core rules

- Respond in Korean when working in this repository.
- Follow the repository AGENTS.md rules first, especially naming, `var`, single-line control statements, and no build unless explicitly requested.
- Use standard BCP-47 language tags. Remember `zh-Hans` means Simplified Chinese and `zh-Hant` means Traditional Chinese.
- Preserve all existing resource keys exactly. Adding a language must not rename keys.
- Prefer copying the complete source `.resw` structure and replacing only `<value>` text.
- Do not update generated `obj` files.

## Files to check

For this repository, language support normally spans these files:

- `CliAccountSwitcher.WinUI/Services/LocalizationService.cs`
- `CliAccountSwitcher.WinUI/Services/ApplicationSettingsService.cs`
- `CliAccountSwitcher.WinUI/Pages/SettingsPage.xaml`
- `CliAccountSwitcher.WinUI/Pages/SettingsPage.xaml.cs`
- `CliAccountSwitcher.WinUI/Strings/<language-tag>/Resources.resw`

If the project structure differs, search for these concepts instead:

- `ApplicationLanguages.PrimaryLanguageOverride`
- supported language tag lists
- saved language override normalization
- language selection `ComboBoxItem` entries
- `GetLanguageSelectedIndex` or equivalent selection mapping
- `GetLanguageOverrideFromSelectedIndex` or equivalent selected-index-to-language mapping
- `Strings/*/Resources.resw`

## Workflow

1. Confirm the requested language tags and correct obvious tag/name inversions before editing. For example, if a user says `zh-hans` is Traditional Chinese, clarify or proceed with standard `zh-Hans` as Simplified Chinese only after confirmation.
2. Read the existing localization service and settings language flow once.
3. Read the base `.resw` file once. Prefer `en-US` as the template unless the user asks for another source language.
4. Add each language tag to the supported language list in `LocalizationService`.
5. If the service only exact-matches culture names, add parent-culture fallback so tags such as `zh-CN` can resolve to `zh-Hans` when appropriate.
6. Add each language tag to the settings normalization allow-list so saved overrides are not discarded.
7. Add language choices to the settings UI ComboBox using `x:Uid` keys only. Do not use `Tag` values for language tags; string `Tag` values can fail under NativeAOT type preservation.
8. Update both settings page index mappings to match the ComboBox order: saved language tag to selected index, and selected index back to saved language tag.
9. Add localized language-name resource keys to every existing `.resw` file, not only the new ones.
10. Create `Strings/<language-tag>/Resources.resw` for each new language with the full key set from the template.
11. Do not build or validate unless the user explicitly asks.
12. If any XAML file changes, tell the user to run their XAML Formatter on the changed XAML files.

## Resource file guidance

- Keep the XML header and RESW schema from the template.
- Keep every `<data name="..." xml:space="preserve">` entry.
- Translate only the `<value>` content.
- Preserve placeholders exactly: `{0}`, `{1}`, `{0:hh\:mm\:ss}`, newline placement, percent signs, and product names.
- Keep brand/product names such as `Codex`, `OAuth`, `Microsoft Store`, `GitHub`, `NuGet`, `MIT License`, and plan names like `Free`, `Plus`, `Team`, `Pro Lite`, `Pro` unless the existing locale translates them.
- Escape XML special characters in values: `&`, `<`, `>`.
- Use UTF-8.

## Typical patch pattern

`LocalizationService.cs`:

```csharp
private static readonly List<string> s_supportedLanguageTags = ["en-US", "ko-KR", "ja-JP", "zh-Hans", "zh-Hant"];
```

If broader culture fallback is needed, normalize an incoming tag by exact match first, then by `CultureInfo.GetCultureInfo(languageTag).Parent.Name`.

`ApplicationSettingsService.cs`:

```csharp
private static string NormalizeLanguageOverride(string languageOverride) => languageOverride is "ko-KR" or "en-US" or "ja-JP" or "zh-Hans" or "zh-Hant" ? languageOverride : "";
```

`SettingsPage.xaml`:

```xml
<ComboBoxItem x:Uid="SettingsPage_JapaneseLanguageComboBoxItem" />
<ComboBoxItem x:Uid="SettingsPage_SimplifiedChineseLanguageComboBoxItem" />
<ComboBoxItem x:Uid="SettingsPage_TraditionalChineseLanguageComboBoxItem" />
```

`SettingsPage.xaml.cs`:

```csharp
private static int GetLanguageSelectedIndex(string languageOverride) => languageOverride switch
{
    "ko-KR" => 1,
    "en-US" => 2,
    "ja-JP" => 3,
    "zh-Hans" => 4,
    "zh-Hant" => 5,
    _ => 0
};

private static string GetLanguageOverrideFromSelectedIndex(int selectedIndex) => selectedIndex switch
{
    1 => "ko-KR",
    2 => "en-US",
    3 => "ja-JP",
    4 => "zh-Hans",
    5 => "zh-Hant",
    _ => ""
};
```

Resource keys to add for the language picker:

```xml
<data name="SettingsPage_JapaneseLanguageComboBoxItem.Content" xml:space="preserve">
  <value>Japanese</value>
</data>
<data name="SettingsPage_SimplifiedChineseLanguageComboBoxItem.Content" xml:space="preserve">
  <value>Simplified Chinese</value>
</data>
<data name="SettingsPage_TraditionalChineseLanguageComboBoxItem.Content" xml:space="preserve">
  <value>Traditional Chinese</value>
</data>
```

Translate those language names in every locale.

## Final response checklist

- Mention the language tags added.
- Mention all files changed.
- State that no build was run unless the user explicitly asked for one.
- If XAML changed, remind the user to run XAML Formatter on the changed XAML file.
