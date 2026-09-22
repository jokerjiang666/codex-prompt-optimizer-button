using System.Globalization;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 上下文采集总编排：定位 → 逐来源采集 → 预算裁剪 → 渲染 JSON。
/// 关闭态 Build 直接返回 null 且不触碰任何 I/O（零采集保证）。
/// </summary>
internal sealed class ContextPipeline
{
    private readonly ContextOptions _options;
    private readonly IContextIo _io;
    private readonly ContextLocator _locator;
    private readonly IReadOnlyList<IContextSource> _sources;
    private readonly Func<DateTimeOffset> _now;

    internal ContextPipeline(ContextOptions options, IContextIo io, string codexHome, Func<DateTimeOffset>? now = null)
    {
        _options = options;
        _io = io;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _locator = new ContextLocator(io, codexHome, now);

        var reader = new ContextSessionReader(io);
        _sources =
        [
            new SessionMetaSource(),
            new ThreadTailSource(reader),
            new ToolSummarySource(reader),
            new WorkspaceGitSource(now)
        ];
    }

    /// <summary>从设置创建；总开关关闭时不读取 CODEX_HOME，也不构造任何采集器。</summary>
    internal static ContextPipeline Create(AppSettings settings, IContextIo? io = null)
    {
        var effectiveIo = io ?? DefaultContextIo.Instance;
        var options = ContextOptions.FromSettings(settings);
        var codexHome = options.Enabled ? effectiveIo.GetCodexHome() : string.Empty;
        return new ContextPipeline(options, effectiveIo, codexHome);
    }

    internal ContextOptions Options => _options;

    /// <summary>本轮采集；返回 null 表示"关闭态，什么都没做"。</summary>
    internal ContextSnapshot? Build(string? windowTitle, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = _locator.Locate(windowTitle, _options);
            if (outcome.Session is null) return new ContextSnapshot(string.Empty, [], 0, outcome.Code);

            var session = outcome.Session;
            var payload = new ContextPayload
            {
                ThreadId = session.ThreadId,
                Cwd = session.Cwd,
                CapturedAt = _now().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            };

            foreach (var source in _sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!source.IsEnabled(_options)) continue;

                // 单个来源失败只丢这个来源，不影响其它来源与整体优化。
                try
                {
                    source.Apply(session, _options, payload, _io);
                }
                catch
                {
                    // ignore
                }
            }

            if (payload.IsEmpty) return new ContextSnapshot(string.Empty, [], 0, "ctx=empty");

            ContextBudget.Fit(payload, _options.TokenBudget, () => TokenEstimator.Estimate(ContextRenderer.Render(payload)));

            var json = ContextRenderer.Render(payload);
            var tokens = TokenEstimator.Estimate(json);
            var diagnostic = $"ctx=ready {outcome.Code} sources={payload.Sources.Count} chars={json.Length} tokens={tokens} confidence={session.Confidence}";
            if (payload.TailPartial) diagnostic += " tail-partial";

            return new ContextSnapshot(json, payload.Sources.ToArray(), tokens, diagnostic);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ContextSnapshot(string.Empty, [], 0, $"ctx=failed type={ex.GetType().Name}");
        }
    }
}