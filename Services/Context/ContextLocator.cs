using System.IO;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 精确定位当前会话：UI 标题 → session_index.jsonl → threadId → rollout 文件，
/// 再叠加「唯一性 + 文件新鲜度 + cwd 交叉校验」三重校验。
/// 任一环节不唯一或不一致时一律返回 null（宁可没有上下文，也不要错的上下文）。
/// </summary>
internal sealed class ContextLocator
{
    private const int CandidateLimit = 8;

    private readonly IContextIo _io;
    private readonly Func<DateTimeOffset> _now;
    private readonly SessionIndexStore _index;
    private readonly GlobalStateHints _hints;

    internal ContextLocator(IContextIo io, string codexHome, Func<DateTimeOffset>? now = null)
    {
        _io = io;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _index = new SessionIndexStore(io, now);
        _hints = new GlobalStateHints(io, codexHome, now);
        CodexHome = codexHome;
    }

    internal string CodexHome { get; }

    internal ContextLocateOutcome Locate(string? windowTitle, ContextOptions options)
    {
        var title = Normalize(windowTitle);
        if (title.Length == 0) return ContextLocateOutcome.Fail("ctx=locator-no-title");

        var entries = _index.Load(Path.Combine(CodexHome, "session_index.jsonl"));
        if (entries.Count == 0) return ContextLocateOutcome.Fail("ctx=locator-no-index");

        var (candidates, prefixUsed) = Match(entries, title);
        if (candidates.Count == 0) return ContextLocateOutcome.Fail("ctx=locator-no-index");

        var passing = new List<(ContextSession Session, bool Fresh)>();
        var lastCode = "ctx=locator-no-file";

        foreach (var entry in candidates.Take(CandidateLimit))
        {
            var path = FindRollout(entry);
            if (path is null)
            {
                lastCode = "ctx=locator-no-file";
                continue;
            }

            var meta = SessionMetaParser.TryParse(SafeReadFirstLine(path));
            if (meta is null)
            {
                lastCode = "ctx=format-unknown";
                continue;
            }

            DateTimeOffset lastWrite;
            try
            {
                lastWrite = _io.GetLastWriteUtc(path);
            }
            catch
            {
                lastCode = "ctx=locator-unreadable";
                continue;
            }

            // 新鲜度：只在"重名需要裁决"时作为硬条件（见下方多候选分支）。
            var fresh = _now() - lastWrite <= TimeSpan.FromSeconds(options.StaleSeconds);

            // cwd 交叉校验：全局状态给出的工作区根必须与会话自述一致。
            var hint = _hints.TryGetWorkspaceRoot(entry.Id);
            var confidence = "medium";
            if (!string.IsNullOrWhiteSpace(hint))
            {
                if (!PathsCompatible(hint!, meta.Cwd))
                {
                    lastCode = "ctx=locator-veto reason=cwd";
                    continue;
                }

                confidence = "high";
            }

            passing.Add((new ContextSession(
                entry.Id,
                path,
                meta.Cwd,
                meta.WorkspaceRoots,
                meta.Branch,
                meta.CommitHash,
                meta.Instructions,
                lastWrite,
                confidence), fresh));
        }

        if (passing.Count == 0) return ContextLocateOutcome.Fail(lastCode);

        // 标题唯一命中：该文件就是当前会话。文件较旧只写进诊断、不否决，
        // 否则"隔一段时间回到旧会话继续优化"会永远拿不到上下文。
        if (passing.Count == 1)
        {
            var single = passing[0];
            var code = prefixUsed ? "ctx=locator-prefix n=1" : "ctx=locator-ok";
            var age = _now() - single.Session.LastWriteUtc;
            if (age > TimeSpan.FromSeconds(options.StaleSeconds)) code += $" stale age={(int)Math.Max(0, age.TotalSeconds)}s";
            if (_hints.LastReadFailed) code += " global-state-unreadable";
            return new ContextLocateOutcome(single.Session, code);
        }

        // 多个候选（标题重名）：只有"唯一新鲜"的候选才能裁决，否则一律放弃。
        var freshCandidates = passing.Where(item => item.Fresh).ToList();
        if (freshCandidates.Count == 1)
            return new ContextLocateOutcome(freshCandidates[0].Session, $"ctx=locator-fresh-tiebreak n={passing.Count}");
        if (freshCandidates.Count == 0)
            return ContextLocateOutcome.Fail($"ctx=locator-stale-all n={passing.Count}");
        return ContextLocateOutcome.Fail($"ctx=locator-ambiguous n={freshCandidates.Count}");
    }

    /// <summary>标题归一化：去首尾空白、压空白、去掉 UI 截断省略号。</summary>
    internal static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var text = title.Trim();
        while (text.EndsWith("…", StringComparison.Ordinal) || text.EndsWith("...", StringComparison.Ordinal))
            text = text[..^1].TrimEnd();
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private static (List<SessionIndexEntry> Candidates, bool PrefixUsed) Match(IReadOnlyList<SessionIndexEntry> entries, string title)
    {
        var exact = entries
            .Where(entry => string.Equals(Normalize(entry.Title), title, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.UpdatedAt)
            .ToList();
        if (exact.Count > 0) return (exact, false);

        // 标题被 UI 截断时用前缀匹配；仍然多义时由上层按"冲突即放弃"处理。
        var prefix = entries
            .Where(entry =>
            {
                var candidate = Normalize(entry.Title);
                return candidate.Length > 0 && candidate.StartsWith(title, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(entry => entry.UpdatedAt)
            .ToList();
        return (prefix, prefix.Count > 0);
    }

    private string? FindRollout(SessionIndexEntry entry)
    {
        var sessions = Path.Combine(CodexHome, "sessions");
        var archived = Path.Combine(CodexHome, "archived_sessions");
        var pattern = "*" + entry.Id + "*.jsonl";

        if (entry.UpdatedAt != DateTimeOffset.MinValue)
        {
            var local = entry.UpdatedAt.ToLocalTime();
            var dayDirectory = Path.Combine(sessions, local.ToString("yyyy"), local.ToString("MM"), local.ToString("dd"));
            var hit = FirstRollout(dayDirectory, pattern, entry.Id, SearchOption.TopDirectoryOnly);
            if (hit is not null) return hit;
        }

        var archivedHit = FirstRollout(archived, pattern, entry.Id, SearchOption.TopDirectoryOnly);
        if (archivedHit is not null) return archivedHit;

        return FirstRollout(sessions, pattern, entry.Id, SearchOption.AllDirectories);
    }

    private string? FirstRollout(string directory, string pattern, string threadId, SearchOption option)
    {
        if (!_io.DirectoryExists(directory)) return null;

        foreach (var file in _io.EnumerateFiles(directory, pattern, option))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains(threadId, StringComparison.OrdinalIgnoreCase)) continue;
            return file;
        }

        return null;
    }

    private string? SafeReadFirstLine(string path)
    {
        try
        {
            return _io.ReadFirstLine(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 工作区根与会话 cwd 的兼容判定：完全一致，或"工作区根是 cwd 的祖先目录"都算一致。
    /// 后者对应 projectless 会话（cwd 是 Documents\Codex 下自动生成的子目录，而全局状态里的根是它的父目录）。
    /// </summary>
    private static bool PathsCompatible(string workspaceRoot, string cwd)
    {
        static string Normalize(string value) => value.Trim().TrimEnd('\\', '/').Replace('/', '\\');

        var root = Normalize(workspaceRoot);
        var target = Normalize(cwd);
        if (root.Length == 0 || target.Length == 0) return false;
        if (string.Equals(root, target, StringComparison.OrdinalIgnoreCase)) return true;
        return target.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
               || root.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase);
    }
}