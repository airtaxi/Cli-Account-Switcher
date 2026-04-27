# Codex Account Switch WinUI

🌐 [한국어](README.ko.md)

Codex Account Switch WinUI is a Windows desktop utility for managing multiple Codex authentication accounts and quickly switching the account currently used by Codex.

The app stores saved account records in its own local application data folder and switches the active Codex account by writing the selected authentication document to `%USERPROFILE%\.codex\auth.json`.

## Features

- Add Codex accounts with OAuth sign-in, the current `auth.json`, a JSON file, or pasted authentication JSON.
- Switch the active Codex account from the Accounts screen.
- View Codex plan information and remaining usage for the 5-hour and weekly windows.
- Click the tray icon to open a quick active account usage popup without opening the full window.
- Refresh account usage and detect expired accounts.
- Back up and restore saved accounts.

## Screens

- **Dashboard**: active account summary, average remaining usage, and low-usage account list.
- **Accounts**: searchable and filterable account list with switch, refresh, rename, delete, backup, and restore actions.
- **Tray icon popup**: quick active account quota view with refresh support from the tray icon.
- **Settings**: language, theme, startup launch, update checks, refresh intervals, warning thresholds, notifications, and settings import/export.
- **About**: app version and third-party license information.

## Basic Workflow

1. Add one or more Codex accounts from the Accounts screen.
2. Review plan and usage information after the app validates each account.
3. Select an account and switch it to make it the active Codex account.
4. Restart Codex when prompted so running Codex processes pick up the newly written `auth.json`.
5. Use backup/export features when moving accounts or settings to another Windows installation.

Switching an account overwrites `%USERPROFILE%\.codex\auth.json`. If you already manage that file manually or with another tool, make a backup first.

## Requirements

- Windows 10 version 1809 or later.
- For development: .NET 10 SDK, Windows App SDK, and Visual Studio with WinUI/MSIX tooling.

## Development

The repository contains three projects:

| Project | Description |
| --- | --- |
| `CodexAccountSwitch.WinUI` | Packaged WinUI 3 desktop app. |
| `CodexAccountSwitch.Api` | Codex OAuth, authentication document, usage, models, and API client helpers. |
| `CodexAccountSwitch.Api.Test` | Console experiment project for Codex API behavior. |

The WinUI app targets `net10.0-windows10.0.26100.0`, enables NativeAOT publishing, uses MSIX tooling, and supports `x86`, `x64`, and `ARM64` package bundles.

Publish profiles live in `CodexAccountSwitch.WinUI/Properties/PublishProfiles`.

## Localization

The app currently includes localized resources for:

- English (`en-US`)
- Korean (`ko-KR`)
- Japanese (`ja-JP`)
- Simplified Chinese (`zh-Hans`)
- Traditional Chinese (`zh-Hant`)

## Acknowledgements

- This project was generated with help from OpenAI Codex.
- This project is inspired by [isxlan0/Codex_AccountSwitch](https://github.com/isxlan0/Codex_AccountSwitch).
- Thanks to the DevWinUI project for providing high-quality WinUI controls.

## License

This project is licensed under the [MIT License](LICENSE).
