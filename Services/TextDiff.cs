namespace CodexInputEnhancer.Services;

internal enum DiffKind
{
    Same,
    Added,
    Removed
}

internal sealed record DiffLine(string Text, DiffKind Kind);

internal sealed record DiffResult(IReadOnlyList<DiffLine> Original, IReadOnlyList<DiffLine> Updated);

/// <summary>
/// 按行做轻量 diff：只标出「这一行在对面不存在」，用于预览窗高亮。
/// 不做 LCS，够用且没有额外依赖。
/// </summary>
internal static class TextDiff
{
    internal static DiffResult Compute(string? original, string? updated)
    {
        var left = SplitLines(original);
        var right = SplitLines(updated);

        var leftSet = new HashSet<string>(left);
        var rightSet = new HashSet<string>(right);

        var originalLines = left
            .Select(line => new DiffLine(line, rightSet.Contains(line) ? DiffKind.Same : DiffKind.Removed))
            .ToList();

        var updatedLines = right
            .Select(line => new DiffLine(line, leftSet.Contains(line) ? DiffKind.Same : DiffKind.Added))
            .ToList();

        return new DiffResult(originalLines, updatedLines);
    }

    private static List<string> SplitLines(string? text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
}