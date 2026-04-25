# Codex Account Switch WinUI

🌐 [English](README.md)

로컬 Codex 계정 백업과 전환을 관리하는 C# / WinUI 3 앱입니다. [isxlan0/Codex_AccountSwitch](https://github.com/isxlan0/Codex_AccountSwitch)에서 아이디어를 얻었고, WebView UI 대신 WinUI 프론트엔드와 MVVM 구조로 작성되었습니다.

## 기능

- 현재 `%USERPROFILE%\.codex\auth.json`을 로컬 계정 프로필로 백업합니다.
- 붙여넣은 `auth.json` 내용을 이름이 있는 계정으로 가져옵니다.
- 선택한 백업을 `%USERPROFILE%\.codex\auth.json`에 기록하여 활성 Codex 계정을 전환합니다.
- 로컬 계정 백업 이름 변경과 삭제를 지원합니다.
- 계정 백업 ZIP 가져오기와 내보내기를 지원합니다.
- 향후 프록시 전달 구현을 위한 API 프록시 설정을 저장합니다.
- 고정 크기 창이 아닌 크기 조절 가능한 WinUI 3 셸을 사용하며, 로딩 시각 요소에는 DevWinUI를 활용합니다.

## 데이터 경로

런타임 데이터는 로컬에 저장됩니다.

- `%LOCALAPPDATA%\Codex Account Switch\config.json`
- `%LOCALAPPDATA%\Codex Account Switch\backups\personal\<account>\auth.json`
- 계정 전환 시 `%USERPROFILE%\.codex\bak\<timestamp>\auth.json`

## 기술 스택

- C# / WinUI 3
- Windows App SDK
- `CommunityToolkit.Mvvm` 기반 MVVM
- DevWinUI 컨트롤 및 리소스
- MIT License

## 빌드

Visual Studio에서 `CodexAccountSwitch.WinUI.slnx`를 연 뒤 구성된 플랫폼 중 하나로 빌드합니다.

- `x86`
- `x64`
- `ARM64`

CLI 빌드에서는 Runtime Identifier를 명시합니다.

```powershell
dotnet build .\CodexAccountSwitch.WinUI\CodexAccountSwitch.WinUI.csproj -c Debug -p:Platform=ARM64 -r win-arm64
```

## 어크놀러지먼트

- 이 프로젝트는 OpenAI Codex의 도움을 받아 생성되었습니다.
- [isxlan0/Codex_AccountSwitch](https://github.com/isxlan0/Codex_AccountSwitch)를 참고했습니다.
- 고품질 WinUI 컨트롤을 제공한 DevWinUI 프로젝트에 감사드립니다.
- MVVM 소스 제너레이터 워크플로를 제공한 CommunityToolkit.Mvvm 프로젝트에 감사드립니다.

## 라이선스

이 프로젝트는 [MIT License](LICENSE)를 따릅니다.
