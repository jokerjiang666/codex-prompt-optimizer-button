# codex提示词内容优化按钮

> 一个运行在 Windows 上的 Codex Desktop 输入增强器：在 Codex 输入框旁提供一个星芒按钮，点击后调用本机 Codex CLI 或 OpenAI 兼容 API，把当前草稿优化成更清晰、可直接执行的提示词。

[English README](README.en.md)

## 功能

- 读取 Codex Desktop 当前输入框草稿。
- 调用本机 Codex CLI（默认）或 OpenAI 兼容 API 优化文本。
- 优化成功后替换草稿，但不会自动发送。
- 支持逐级撤回；发送或清空草稿后自动隐藏撤回按钮。
- 支持最近发送记录，使用 Windows DPAPI 加密保存在本机。
- 支持在设置面板自定义优化提示词。
- 仅在 Codex 窗口处于前台时显示浮层。

## 系统要求

- Windows 10 19041 或更高版本。
- 如果使用框架依赖包，需要安装 .NET 8 Desktop Runtime。
- 使用 Codex CLI 模式时，本机需要安装并登录 Codex Desktop。
- 使用 API 模式时，需要一个 OpenAI 兼容 API 服务。

## 下载

前往 Releases 下载最新版：

- 自包含包：解压后直接运行，无需额外安装 .NET。
- 框架依赖包：需要先安装 .NET 8 Desktop Runtime。

## 使用

1. 解压并运行 `CodexInputEnhancer.exe`。
2. 打开 Codex Desktop。
3. 在输入框左下角“完全访问/访问权限”右侧会出现星芒图标。
4. 输入草稿，点击星芒进行优化。
5. 优化成功后草稿会被替换；点击回转箭头可逐级撤回。
6. 右键星芒可打开设置、查看最近发送、清空记录或退出。

## 配置

- 默认使用本机 Codex CLI。
- 可以在设置面板切换到 OpenAI-compatible API。
- API Key 使用 Windows 当前用户 DPAPI 加密保存，不会写入 `settings.json` 或日志。
- 首次启动会自动创建 `data/settings.json`。
- 请填写自己的 API Base URL、Model 和 API Key；仓库和 Release 不包含任何密钥或私有 API 地址。

## 隐私与安全

- 仓库和 Release 不包含 API Key、token、Cookie、私钥或私有 API 地址。
- API Key 仅以 DPAPI 加密形式保存在本机。
- 撤回历史只保存在当前进程内存中。
- `runtime-status.log` 仅记录连接/窗口状态和错误类型，不记录输入内容。
- 使用 API 模式时，输入内容会发送到你配置的 API 服务。

## 从源码构建

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

需要 .NET 8 SDK 和 Windows Desktop 开发组件。

## 已知限制

- 仅支持 Windows。
- 依赖 Codex Desktop 的 UIAutomation 结构；Codex 更新后可能需要适配。
- 使用 API 模式时，请自行确认目标服务的隐私和数据保留政策。
