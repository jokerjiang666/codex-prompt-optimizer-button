using System.IO;

namespace CodexInputEnhancer.Services;

/// <summary>L4：工作区 git 摘要（status / diff --stat / log -1），带 60 秒缓存与超时降级。</summary>
internal sealed class WorkspaceGitSource : IContextSource
{
    private const int TimeoutMs = 800;
    private const int MaxStatusLines = 12;
    private const int MaxDiffStatLines = 12;
    private const int MaxLogLines = 3;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly Func<DateTimeOffset> _now;
    private string? _cachedCwd;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private (string? Status, string? DiffStat, string? Log) _cached;

    internal WorkspaceGitSource(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string Name => "workspace-git";

    public bool IsEnabled(ContextOptions options) => options.Git;

    public void Apply(ContextSession session, ContextOptions options, ContextPayload payload, IContextIo io)
    {
        var cwd = string.IsNullOrWhiteSpace(payload.Cwd) ? session.Cwd : payload.Cwd!;
        if (string.IsNullOrWhiteSpace(cwd)) return;

        string? status;
        string? diffStat;
        string? log;

        if (string.Equals(_cachedCwd, cwd, StringComparison.OrdinalIgnoreCase) && _now() - _cachedAt < CacheTtl)
        {
            (status, diffStat, log) = _cached;
        }
        else
        {
            if (!io.DirectoryExists(cwd)) return;

            // 非 git 目录（含裸目录 / 已删除的 .git）直接跳过，不启动任何子进程。
            var gitEntry = Path.Combine(cwd, ".git");
            if (!io.DirectoryExists(gitEntry) && !io.FileExists(gitEntry)) return;

            status = Limit(io.RunProcess("git", "-c core.quotepath=false status --porcelain=v1", cwd, TimeoutMs), MaxStatusLines);
            diffStat = Limit(io.RunProcess("git", "-c core.quotepath=false diff --stat", cwd, TimeoutMs), MaxDiffStatLines);
            log = Limit(io.RunProcess("git", "-c core.quotepath=false log -1 --oneline", cwd, TimeoutMs), MaxLogLines);

            _cached = (status, diffStat, log);
            _cachedCwd = cwd;
            _cachedAt = _now();
        }

        payload.GitStatus = status;
        payload.GitDiffStat = diffStat;
        payload.GitLog = log;

        if (status is not null || diffStat is not null || log is not null) payload.Sources.Add(Name);
    }

    private static string? Limit(string? output, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .Take(maxLines)
            .ToArray();
        if (lines.Length == 0) return null;
        return ContextRedactor.Redact(string.Join('\n', lines));
    }
}