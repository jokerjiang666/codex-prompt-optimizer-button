namespace CodexInputEnhancer.Services;

/// <summary>L1：会话元信息（cwd / 工作区根 / 分支与提交 / 项目指令摘要）。</summary>
internal sealed class SessionMetaSource : IContextSource
{
    private const int MaxInstructionChars = 1200;

    public string Name => "session-meta";

    public bool IsEnabled(ContextOptions options) => options.WorkspaceMeta;

    public void Apply(ContextSession session, ContextOptions options, ContextPayload payload, IContextIo io)
    {
        payload.Cwd = string.IsNullOrWhiteSpace(payload.Cwd) ? session.Cwd : payload.Cwd;
        payload.Branch = session.Branch;
        payload.Commit = ShortCommit(session.CommitHash);

        var instructions = ContextRedactor.Truncate(ContextRedactor.Redact(session.Instructions), MaxInstructionChars);
        payload.InstructionsSummary = instructions.Length == 0 ? null : instructions;

        foreach (var root in session.WorkspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (!payload.WorkspaceRoots.Contains(root, StringComparer.OrdinalIgnoreCase)) payload.WorkspaceRoots.Add(root);
        }

        payload.Sources.Add(Name);
    }

    private static string? ShortCommit(string? commit) =>
        string.IsNullOrWhiteSpace(commit) ? null : commit!.Length > 12 ? commit[..12] : commit;
}