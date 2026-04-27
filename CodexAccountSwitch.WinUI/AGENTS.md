# CodexAccountSwitch.WinUI Agent Guide

## What Makes This WinUI Project Different

This is not a plain WinUI template application. It is a packaged, NativeAOT-oriented WinUI 3 desktop app for managing Codex authentication accounts and switching the active Codex account by writing `auth.json` under the user's `.codex` directory.

Key differences from a typical WinUI project:

- `Program.cs` owns startup because `CodexAccountSwitch.WinUI.csproj` defines `DISABLE_XAML_GENERATED_MAIN`.
- Startup is single-instance. `Program.Main` registers `CodexAccountSwitchWinUI_SingleInstance`; secondary activations are redirected to the current instance.
- `App.xaml.cs` is the service composition root. It creates and stores static service instances, starts background services, handles startup-task launches, handles notification activations, and creates `MainWindow`.
- The app is tray-aware. `Views/MainWindow.xaml` uses `H.NotifyIcon.WinUI`; closing the main window hides it instead of exiting unless the close comes from system shutdown or the tray Exit command.
- The main window derives from `WinUIEx.WindowEx`, uses a custom WinUI `TitleBar`, and hosts page navigation inside `AppFrame`.
- Main navigation is split between `Views/MainWindow.xaml.cs` and `Pages/MainPage.xaml.cs`. `MainWindow` owns the top selector and broadcasts `MainPageNavigationSection` changes through `WeakReferenceMessenger`; `MainPage` swaps the section page in `SectionContentFrame`.
- The app uses DevWinUI controls and visuals, including `SelectorBarSegmented`, `BlurEffectControl`, and `LoadingIndicator`.
- The project targets `net10.0-windows10.0.26100.0`, uses `LangVersion` `preview`, has `PublishAot` enabled, and packages through MSIX tooling.
- The WinUI project references `..\CodexAccountSwitch.Api\CodexAccountSwitch.Api.csproj` for Codex API, OAuth, authentication document, and usage client behavior.

## Runtime Flow

When the app starts:

1. `Program.Main` initializes WinRT COM wrappers, registers or redirects the single app instance, then starts `App`.
2. `App.App()` initializes settings, localization, theme, notification, startup registration, store update, account, and restart services.
3. `StoreUpdateService.Start()` and `CodexAccountService.Start()` begin background work before the main window is shown.
4. `App.OnLaunched` creates `MainWindow`. Startup-task activations keep the app in the tray unless the app has been explicitly asked to show the window.
5. `MainWindow` configures icon, title bar, tray commands, window subclassing, theme, localization, and navigates `AppFrame` to `MainPage`.
6. `MainPage` defaults to `DashboardPage`; selector changes navigate to `DashboardPage`, `AccountsPage`, `AboutPage`, or `SettingsPage`.

Notification activation can request navigation to the Accounts section. If the window or `MainWindow.Instance` is not ready yet, `App` stores the pending navigation and applies it after launch completes.

## Account And Data Model

- The active Codex account is the authentication document stored at `Constants.CurrentAuthenticationFilePath`, which resolves to `%USERPROFILE%\.codex\auth.json`.
- App-owned data is stored under `Constants.UserDataDirectory`, which resolves to `%LOCALAPPDATA%\CodexAccountSwitch.WinUI`.
- Saved accounts are persisted in `accounts.json`; settings are persisted in `config.json`; backups default to the `backups` subfolder.
- `CodexAccountService` keeps an in-memory account list guarded by `_accountsLock`, persists through `CodexAccountJsonSerializerContext`, and watches `.codex\auth.json` with `FileSystemWatcher` to detect active-account changes.
- Usage refresh runs in background loops with separate active-account and inactive-account intervals. Settings changes reset those schedules.
- Keep serialization NativeAOT-safe. Add new serialized model types to `Helpers/CodexAccountJsonSerializerContext.cs` instead of relying on reflection-based JSON serialization.

## Localization

- User-facing strings live in `Strings/en-US/Resources.resw` and `Strings/ko-KR/Resources.resw`.
- XAML should prefer `x:Uid` where practical.
- Code-behind localization should go through `App.LocalizationService`.
- `LocalizationService` uses `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader`, not UWP-era resource APIs.
- When adding or changing user-visible text, update both English and Korean resources unless the string is intentionally not localized.

## NuGet And License Metadata

When updating, adding, or removing any NuGet package in `CodexAccountSwitch.WinUI.csproj`, also update `Pages/AboutPage.xaml.cs`.

Specifically, keep `s_thirdPartyLicensePackages` and `CreateThirdPartyLicensePackages()` synchronized with every package reference version, license, author, and project URL shown in the About page's third-party licenses dialog. This is required; do not leave package versions in the project file and About page out of sync.

## Project Structure

- `Controls/`: reusable WinUI controls, currently including active account quota UI.
- `Dialogs/`: dialog shells and dialog contexts, including account addition and third-party licenses.
- `Helpers/`: NativeAOT-safe JSON context plus window, taskbar, and dialog helpers.
- `Models/`: account, settings, message, navigation, backup, and license data models.
- `Pages/`: navigable app pages and add-account wizard pages.
- `Services/`: application state, localization, notifications, startup registration, store updates, Codex account orchestration, and Codex restart behavior.
- `ViewModels/`: CommunityToolkit.Mvvm view models for pages and account presentation.
- `Views/`: top-level windows such as `MainWindow` and `PopupWindow`.

## Coding Rules For This Repository

- Use full, unabbreviated names for variables, methods, and properties.
- Prefer `var` when declaring local variables.
- Use private instance fields named `_camelCase` and private static fields named `s_camelCase`.
- Use PascalCase for classes, properties, methods, and enums.
- Keep single-statement `if`, `for`, `foreach`, and `while` bodies on the same physical line without braces.
- Use expression-bodied syntax for single-line methods.
- Keep short method calls, short method definitions, and short argument lists on one line.
- Prefer primary constructors and collection expressions where they fit the existing code.
- Event handlers should use `On{ControlName}{EventName}`; for click events use `Clicked`, for example `OnAddAccountButtonClicked`.
- Preserve the comment language and style of the file being edited.
- Do not run builds unless explicitly requested or the change is large enough that verification is necessary.
- Do not commit or push unless explicitly requested.

## XAML Guidance

- Prefer container `Spacing`, `RowSpacing`, and `ColumnSpacing` for layout gaps. Use `Margin` only for individual positional adjustment.
- Match the current visual stack: WinUI 3, WinUIEx windows, DevWinUI controls, Mica backdrop, custom title bar, and resource-based localization.
- After changing XAML, remind the user to run their XAML formatter on the changed XAML files.

## Build And Packaging Notes

- The app is configured for `x86`, `x64`, and `ARM64` platforms with runtime identifiers `win-x86`, `win-x64`, and `win-arm64`.
- Publish profiles live under `Properties/PublishProfiles`.
- The project uses MSIX packaging and `AppxBundle` is set to `Always`.
- If `DOTNET_CLI_HOME` is set for a build, remove that temporary directory immediately after the build finishes.
