namespace CodexInputEnhancer.Services;

/// <summary>
/// 粗粒度 token 估算：CJK/全角字符按 1 token/字，其余按 4 字符/token。
/// 只用于预算裁剪，不追求与真实 tokenizer 一致（目标误差 ≤20%）。
/// </summary>
internal static class TokenEstimator
{
    internal static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        double wide = 0;
        double narrow = 0;
        foreach (var ch in text)
        {
            if (ch >= 0x2E80) wide++;
            else narrow++;
        }
        return (int)Math.Ceiling(wide + narrow / 4.0);
    }
}

/// <summary>
/// 预算裁剪：依次牺牲 git diff 摘要 → 工具摘要 → 最旧的对话 → 指令摘要 → git 其余摘要，
/// 直到渲染结果落进预算。草稿原文不归本类管，因此永不会被裁。
/// </summary>
internal static class ContextBudget
{
    internal static void Fit(ContextPayload payload, int budget, Func<int> measure)
    {
        var guard = 0;
        while (measure() > budget && guard++ < 200)
        {
            if (payload.GitDiffStat is not null) { payload.GitDiffStat = null; continue; }
            if (payload.Tools.Count > 0) { payload.Tools.RemoveAt(0); continue; }
            if (payload.Messages.Count > 0) { payload.Messages.RemoveAt(0); continue; }
            if (payload.InstructionsSummary is { Length: > 200 })
            {
                payload.InstructionsSummary = payload.InstructionsSummary[..(payload.InstructionsSummary.Length / 2)];
                continue;
            }
            if (payload.InstructionsSummary is not null) { payload.InstructionsSummary = null; continue; }
            if (payload.GitStatus is not null) { payload.GitStatus = null; continue; }
            if (payload.GitLog is not null) { payload.GitLog = null; continue; }
            break;
        }
    }
}