# Codex Prompt Optimizer Button

> A Windows input enhancer for Codex Desktop. It adds a star button next to the Codex composer. Click it to send the current draft to the local Codex CLI or an OpenAI-compatible API and replace it with a clearer, execution-ready prompt.

[中文说明](README.md)

## Features

- Reads the current Codex Desktop composer draft.
- Optimizes via the local Codex CLI (default) or an OpenAI-compatible API.
- Replaces the draft without sending it automatically.
- Supports step-by-step undo; hides the undo button after send or draft clear.
- Keeps recent-send history encrypted with Windows DPAPI.
- Lets you customize the optimization prompt in Settings.
- Shows the overlay only when Codex is in the foreground.

## Requirements

- Windows 10 19041 or later.
- .NET 8 Desktop Runtime when using the framework-dependent build.
- Codex Desktop installed and signed in when using Codex CLI mode.
- An OpenAI-compatible API endpoint when using API mode.

## Download

Get the latest build from Releases:

- Self-contained: unzip and run; no .NET installation required.
- Framework-dependent: requires the .NET 8 Desktop Runtime.

## Usage

1. Unzip and run `CodexInputEnhancer.exe`.
2. Open Codex Desktop.
3. A star icon appears next to the "Full access/Access" control in the composer.
4. Type a draft and click the star to optimize it.
5. The draft is replaced; click the rotate icon to undo step by step.
6. Right-click the star to open Settings, view recent sends, clear history, or exit.

## Configuration

- Uses the local Codex CLI by default.
- Switch to an OpenAI-compatible API in Settings.
- API keys are encrypted with Windows DPAPI for the current user; they are never written to `settings.json` or logs.
- `data/settings.json` is created automatically on first launch.
- Configure your own API Base URL, model, and API key. The repository and releases do not contain any secrets or private API endpoints.

## Privacy and Security

- No API keys, tokens, cookies, private keys, or private API endpoints are included in this repository or its releases.
- API keys are stored locally using DPAPI.
- Undo history stays in memory.
- `runtime-status.log` records connection/window state and error types only, never input content.
- In API mode, input content is sent to the endpoint you configure.

## Build from Source

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

Requires the .NET 8 SDK and Windows Desktop development tools.

## Known Limitations

- Windows only.
- Relies on Codex Desktop's UIAutomation structure; may need updates when Codex changes.
- In API mode, review the target service's privacy and data-retention policies.

## Acknowledgments

Thanks to the [LINUX DO](https://linux.do/) community for providing an open and friendly technical exchange platform.
