# Codex Account Switch WinUI

🌐 [한국어](README.ko.md)

A C# and WinUI 3 account manager for local Codex account backups and switching. It is inspired by [isxlan0/Codex_AccountSwitch](https://github.com/isxlan0/Codex_AccountSwitch), while using a native WinUI frontend and MVVM architecture instead of a WebView UI.

## Features

- Backup the current `%USERPROFILE%\.codex\auth.json` into local account profiles.
- Import pasted `auth.json` content as a named account.
- Switch the active Codex account by writing the selected backup to `%USERPROFILE%\.codex\auth.json`.
- Rename and delete local account backups.
- Import and export account backup ZIP files.
- Persist API proxy-related settings for future proxy forwarding work.
- Resizable WinUI 3 shell with DevWinUI-assisted loading visuals.

## Data Directory

Runtime data is stored locally:

- `%LOCALAPPDATA%\Codex Account Switch\config.json`
- `%LOCALAPPDATA%\Codex Account Switch\backups\personal\<account>\auth.json`
- `%USERPROFILE%\.codex\bak\<timestamp>\auth.json` when switching accounts

## Tech Stack

- C# / WinUI 3
- Windows App SDK
- MVVM with `CommunityToolkit.Mvvm`
- DevWinUI controls/resources
- MIT License

## Build

Open `CodexAccountSwitch.WinUI.slnx` in Visual Studio, then build one of the configured platforms:

- `x86`
- `x64`
- `ARM64`

For CLI builds, pass the runtime identifier explicitly, for example:

```powershell
dotnet build .\CodexAccountSwitch.WinUI\CodexAccountSwitch.WinUI.csproj -c Debug -p:Platform=ARM64 -r win-arm64
```

## Acknowledgements

- This project was generated with help from OpenAI Codex.
- This project references [isxlan0/Codex_AccountSwitch](https://github.com/isxlan0/Codex_AccountSwitch).
- Thanks to the DevWinUI project for providing high-quality WinUI controls.
- Thanks to the CommunityToolkit.Mvvm project for the MVVM source generator workflow.

## License

This project is licensed under the [MIT License](LICENSE).
