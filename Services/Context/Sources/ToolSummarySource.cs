namespace CodexInputEnhancer.Services;

/// <summary>L3：最近工具调用摘要（只留工具名 + 命令/路径线索，绝不带完整输出）。</summary>
internal sealed class ToolSummarySource : IContextSource
{
    private const int ToolLimit = 2;

    private readonly ContextSessionReader _reader;

    internal ToolSummarySource(ContextSessionReader reader)
    {
        _reader = reader;
    }

    public string Name => "tool-summary";

    public bool IsEnabled(ContextOptions options) => options.ThreadTail;

    public void Apply(ContextSession session, ContextOptions options, ContextPayload payload, IContextIo io)
    {
        var tail = _reader.ReadTail(session.RolloutPath, 0, ToolLimit);
        foreach (var tool in tail.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name)) continue;
            payload.Tools.Add(tool);
        }

        payload.TailPartial = payload.TailPartial || tail.DroppedPartialLine;
        if (payload.Tools.Count > 0) payload.Sources.Add(Name);
    }
}