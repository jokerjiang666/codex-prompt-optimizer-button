namespace CodexInputEnhancer.Services;

/// <summary>L2：最近对话尾部（已截断、已脱敏）。</summary>
internal sealed class ThreadTailSource : IContextSource
{
    private readonly ContextSessionReader _reader;

    internal ThreadTailSource(ContextSessionReader reader)
    {
        _reader = reader;
    }

    public string Name => "thread-tail";

    public bool IsEnabled(ContextOptions options) => options.ThreadTail && options.MessageLimit > 0;

    public void Apply(ContextSession session, ContextOptions options, ContextPayload payload, IContextIo io)
    {
        var tail = _reader.ReadTail(session.RolloutPath, options.MessageLimit, 0);
        foreach (var message in tail.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Text)) continue;
            payload.Messages.Add(message);
        }

        payload.TailPartial = payload.TailPartial || tail.DroppedPartialLine;
        if (payload.Messages.Count > 0) payload.Sources.Add(Name);
    }
}