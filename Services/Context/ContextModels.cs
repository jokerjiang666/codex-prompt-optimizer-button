using System.IO;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

/// <summary>上下文采集选项：由 AppSettings 映射而来；Enabled=false 时不允许发生任何 I/O。</summary>
internal sealed record ContextOptions(
    bool Enabled,
    bool ThreadTail,
    bool Git,
    bool WorkspaceMeta,
    int TokenBudget,
    int MessageLimit,
    int StaleSeconds)
{
    internal static ContextOptions FromSettings(AppSettings settings) => new(
        settings.ContextEnabled,
        settings.ContextThreadTailEnabled,
        settings.ContextGitEnabled,
        settings.ContextWorkspaceMetaEnabled,
        Math.Clamp(settings.ContextTokenBudget, 200, 4000),
        Math.Clamp(settings.ContextMessageLimit, 0, 20),
        Math.Clamp(settings.ContextStaleSeconds, 30, 3600));
}

/// <summary>一条对话消息（已截断、已脱敏）。</summary>
internal sealed record ContextMessage(string Role, string Text);

/// <summary>一次工具调用摘要（只保留工具名与命令/路径线索）。</summary>
internal sealed record ContextToolCall(string Name, string Hint);

/// <summary>渲染前的结构化上下文。</summary>
internal sealed class ContextPayload
{
    public string? ThreadId { get; set; }
    public string? Cwd { get; set; }
    public string? CapturedAt { get; set; }
    public bool TailPartial { get; set; }
    public List<string> WorkspaceRoots { get; } = [];
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? InstructionsSummary { get; set; }
    public List<ContextMessage> Messages { get; } = [];
    public List<ContextToolCall> Tools { get; } = [];
    public string? GitStatus { get; set; }
    public string? GitDiffStat { get; set; }
    public string? GitLog { get; set; }
    public List<string> Sources { get; } = [];

    internal bool IsEmpty =>
        string.IsNullOrWhiteSpace(Cwd)
        && string.IsNullOrWhiteSpace(Branch)
        && string.IsNullOrWhiteSpace(Commit)
        && string.IsNullOrWhiteSpace(InstructionsSummary)
        && Messages.Count == 0
        && Tools.Count == 0
        && string.IsNullOrWhiteSpace(GitStatus)
        && string.IsNullOrWhiteSpace(GitDiffStat)
        && string.IsNullOrWhiteSpace(GitLog);
}

/// <summary>定位到的当前会话。</summary>
internal sealed record ContextSession(
    string ThreadId,
    string RolloutPath,
    string Cwd,
    IReadOnlyList<string> WorkspaceRoots,
    string? Branch,
    string? CommitHash,
    string? Instructions,
    DateTimeOffset LastWriteUtc,
    string Confidence);

internal sealed record ContextLocateOutcome(ContextSession? Session, string Code)
{
    internal static ContextLocateOutcome Fail(string code) => new(null, code);
}

/// <summary>一次采集结果；Json 为空表示本轮不带上下文。</summary>
internal sealed record ContextSnapshot(string Json, IReadOnlyList<string> Sources, int EstimatedTokens, string Diagnostic)
{
    internal bool HasContext => Json.Length > 0;
}

internal interface IContextSource
{
    string Name { get; }
    bool IsEnabled(ContextOptions options);
    void Apply(ContextSession session, ContextOptions options, ContextPayload payload, IContextIo io);
}

/// <summary>上下文采集用到的全部外部访问；关闭态下不得被触碰（测试用探针断言）。</summary>
internal interface IContextIo
{
    string GetCodexHome();
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string[] ReadAllLines(string path);
    string ReadAllText(string path);
    string ReadFirstLine(string path);
    TailReadResult ReadFrom(string path, long offset, int maxBytes);
    long GetLength(string path);
    DateTimeOffset GetLastWriteUtc(string path);
    IEnumerable<string> EnumerateFiles(string directory, string pattern, SearchOption option);
    string? RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs);
}

/// <summary>从指定 offset 开始的一次尾部读取结果。</summary>
internal sealed record TailReadResult(IReadOnlyList<string> Lines, long NextOffset, bool Reset);