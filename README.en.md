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
- Adds a "Continue" button: when the composer is empty, it writes your configured continue text (default `继续`) and sends it. The button can be hidden in Settings.
- Optional auto-continue on interruption (off by default): when an interruption/error such as `429 Too Many Requests` is detected, it sends the configured text automatically. Interval defaults to 10 seconds, with no attempt cap.
- **Template library**: eight built-in templates (basic, task breakdown, bug repro, code review, refactor, docs, image prompt, video prompt) plus custom templates and JSON import/export (Settings -> Templates).
- **Deep optimize**: one or two extra iteration rounds on top of the first result (off by default).
- **Preview before apply**: a side-by-side diff window with Apply / Keep original / Rewrite once more / Copy (on by default; Esc keeps the original).
- **Template variables**: `{{name}}` placeholders prompt you for values before optimizing.
- **Tray icon**: visible in the taskbar notification area with Open settings / Exit; exiting now terminates the process.

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
6. Click the double chevron (pointing up) next to the star to send the continue text in one click (the composer must be empty).
7. Right-click the star to open Settings, view recent sends, clear history, or exit.

## Configuration

- Uses the local Codex CLI by default; it loads your own `~/.codex/config.toml` provider and model, so the enhancer follows the Codex app configuration.
- Switch to an OpenAI-compatible API in Settings.
- API keys are encrypted with Windows DPAPI for the current user; they are never written to `settings.json` or logs.
- `data/settings.json` is created automatically on first launch.
- Configure your own API Base URL, model, and API key. The repository and releases do not contain any secrets or private API endpoints.
- Settings > Appearance & Behavior: continue text (default `继续`), auto-continue interval (default 10s), auto-continue on interruption (off by default), and whether to show the Continue button (on by default).

## Privacy and Security

- No API keys, tokens, cookies, private keys, or private API endpoints are included in this repository or its releases.
- API keys are stored locally using DPAPI.
- Undo history stays in memory.
- `runtime-status.log` records connection/window state and error types only, never input content.
- In API mode, input content is sent to the endpoint you configure.
- Auto-continue matches error patterns against conversation text locally; only status and error signatures are logged, never conversation content.

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
- In API mode, review the target service''s privacy and data-retention policies.
- Auto-continue relies on error wording patterns and the UIAutomation structure, so unrecognized error types are ignored; sending relies on the send button''s UIA name/position and falls back to a "press Enter manually" hint.
- Auto-continue is off by default and has no attempt cap: if errors keep occurring it will keep retrying at the configured interval, so watch your quota and turn the switch off when needed.

## Acknowledgments

Thanks to the [LINUX DO](https://linux.do/) community for providing an open and friendly technical exchange platform.
