using System.IO;
using System.Text;
using CodexInputEnhancer.Models;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer.ContextTests;

internal static class Program
{
    private const string Cwd = @"E:\AI\Codex\CodexProject\demo";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);

    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        // 真机探测模式：dotnet ContextTests.dll --probe "会话标题"（只读，用于现场排障/验收）
        if (args.Length > 0 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
            return Probe(args.Length > 1 ? args[1] : null);
        Run("关闭态：零 I/O，且返回 null（不采集）", DisabledPipelineDoesNoIo);
        Run("定位器：标题唯一命中并读出 cwd", LocatorUniqueTitle);
        Run("定位器：重名时优先新鲜会话", LocatorDuplicatePrefersFresh);
        Run("定位器：重名且都新鲜 → 放弃（fail-closed）", LocatorDuplicateAmbiguous);
        Run("定位器：标题被截断 → 前缀匹配", LocatorEllipsisPrefix);
        Run("定位器：cwd 交叉校验冲突 → 否决", LocatorCwdVeto);
        Run("定位器：工作区根是 cwd 的祖先目录 → 视为一致（projectless）", LocatorAncestorRootAccepted);
        Run("定位器：无标题 / 无索引 → 放弃", LocatorNoTitleOrIndex);
        Run("定位器：唯一命中但会话较旧 → 仍可用（诊断标注 stale）", LocatorUniqueButStale);
        Run("定位器：重名且都已过期 → 无法裁决，放弃", LocatorDuplicateAllStale);
        Run("读取器：解析消息与工具摘要，坏行不炸", ReaderParsesTail);
        Run("读取器：消息落在尾部窗口之外时自动扩大窗口", ReaderExpandsWindow);
        Run("预算：超限裁剪后仍在预算内", BudgetTrimsToLimit);
        Run("脱敏：密钥 / Token / 内网地址被替换", RedactorRemovesSecrets);
        Run("组装：带上下文保留草稿并附证据块", ComposeWithContextKeepsDraft);
        Run("组装：不带上下文时与旧行为逐字符一致", ComposeWithoutContextIsUnchanged);
        Run("端到端：开启态采集到 cwd / 最近对话 / git 摘要", EnabledPipelineCollectsContext);

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>用真实 IContextIo 跑一次采集，只打印诊断与将随请求发送的 JSON（已脱敏）。</summary>
    private static int Probe(string? title)
    {
        var io = DefaultContextIo.Instance;
        var options = new ContextOptions(true, true, true, true, 1200, 6, 300);
        var pipeline = new ContextPipeline(options, io, io.GetCodexHome());
        var snapshot = pipeline.Build(title);

        Console.WriteLine($"codexHome={io.GetCodexHome()}");
        Console.WriteLine($"title={(string.IsNullOrWhiteSpace(title) ? "(null)" : title)}");
        Console.WriteLine($"diagnostic={snapshot?.Diagnostic ?? "ctx=off"}");
        Console.WriteLine($"sources={(snapshot is null ? "-" : string.Join(",", snapshot.Sources))}");
        Console.WriteLine($"tokens={snapshot?.EstimatedTokens ?? 0}");
        if (snapshot?.HasContext == true)
        {
            Console.WriteLine("--- context json (截断显示) ---");
            Console.WriteLine(snapshot.Json.Length > 1500 ? snapshot.Json[..1500] + "…" : snapshot.Json);
        }

        return 0;
    }
    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS  {name}");
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  {name}");
            Console.WriteLine($"      {ex.Message}");
            _failed++;
        }
    }

    // ---------------------------------------------------------------- 关闭态

    private static void DisabledPipelineDoesNoIo()
    {
        var (io, _) = BuildEnvironment("任意标题", NewId(4), Cwd, Now.AddSeconds(-5));

        var settings = new AppSettings { ContextEnabled = false };
        var pipeline = ContextPipeline.Create(settings, io);
        var snapshot = pipeline.Build("任意标题");

        Assert.Null(snapshot, "关闭态必须返回 null（表示本轮不带上下文）");
        Assert.Equal(0, io.FileReads, "关闭态不得读取任何文件");
        Assert.Equal(0, io.DirectoryReads, "关闭态不得枚举任何目录");
        Assert.Equal(0, io.ProcessRuns, "关闭态不得启动任何子进程");

        var forbidden = new ForbiddenIo();
        var guarded = ContextPipeline.Create(settings, forbidden);
        Assert.Null(guarded.Build("任意标题"), "关闭态（探针）也必须返回 null");
        Assert.Equal(0, forbidden.Calls, "关闭态不得触碰任何上下文 I/O");
    }

    // ---------------------------------------------------------------- 定位器

    private static void LocatorUniqueTitle()
    {
        const string title = "设计Codex上下文获取开关";
        var id = NewId(1);
        var (io, _) = BuildEnvironment(title, id, Cwd, Now.AddSeconds(-10), hint: Cwd);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.NotNull(outcome.Session, "应当命中唯一会话");
        Assert.Equal(id, outcome.Session!.ThreadId, "threadId 必须来自 session_index");
        Assert.Equal(Cwd, outcome.Session.Cwd, "cwd 必须来自 session_meta");
        Assert.Equal("high", outcome.Session.Confidence, "全局状态与 session_meta 一致时应为 high");
    }

    private static void LocatorDuplicatePrefersFresh()
    {
        const string title = "每周回顾";
        var freshId = NewId(1);
        var staleId = NewId(2);
        var fresh = Now.AddSeconds(-20);
        var stale = Now.AddHours(-6);

        var io = new FakeIo();
        io.AddFile(
            Path.Combine(io.Home, "session_index.jsonl"),
            IndexLine(staleId, title, stale) + "\n" + IndexLine(freshId, title, fresh) + "\n",
            fresh);
        io.AddFile(RolloutPathFor(io, staleId, stale), RolloutBody(Cwd), stale);
        io.AddFile(RolloutPathFor(io, freshId, fresh), RolloutBody(Cwd), fresh);
        io.AddFile(Path.Combine(io.Home, ".codex-global-state.json"), Hints((staleId, Cwd), (freshId, Cwd)), fresh);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.NotNull(outcome.Session, "重名时应命中唯一新鲜的那条");
        Assert.Equal(freshId, outcome.Session!.ThreadId, "必须选择新鲜会话而不是最新索引顺序");
    }

    private static void LocatorDuplicateAmbiguous()
    {
        const string title = "每周回顾";
        var firstId = NewId(1);
        var secondId = NewId(2);
        var first = Now.AddSeconds(-15);
        var second = Now.AddSeconds(-25);

        var io = new FakeIo();
        io.AddFile(
            Path.Combine(io.Home, "session_index.jsonl"),
            IndexLine(firstId, title, first) + "\n" + IndexLine(secondId, title, second) + "\n",
            first);
        io.AddFile(RolloutPathFor(io, firstId, first), RolloutBody(Cwd), first);
        io.AddFile(RolloutPathFor(io, secondId, second), RolloutBody(Cwd), second);
        io.AddFile(Path.Combine(io.Home, ".codex-global-state.json"), Hints((firstId, Cwd), (secondId, Cwd)), first);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.Null(outcome.Session, "两条都新鲜且无法区分时必须放弃上下文");
        Assert.Contains("ambiguous", outcome.Code, "诊断码应说明歧义");
    }

    private static void LocatorEllipsisPrefix()
    {
        const string title = "设计Codex上下文获取开关";
        var id = NewId(1);
        var (io, _) = BuildEnvironment(title, id, Cwd, Now.AddSeconds(-10), hint: Cwd);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate("设计Codex上下文获取…", Options());

        Assert.NotNull(outcome.Session, "标题被 UI 截断时应能前缀匹配");
        Assert.Contains("prefix", outcome.Code, "诊断码应标记前缀匹配");
    }

    private static void LocatorCwdVeto()
    {
        const string title = "上下文开关";
        var id = NewId(1);
        var (io, _) = BuildEnvironment(title, id, Cwd, Now.AddSeconds(-10), hint: @"E:\another\project");

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.Null(outcome.Session, "cwd 交叉校验冲突时必须放弃");
        Assert.Contains("veto", outcome.Code, "诊断码应标记被否决");
    }

    private static void LocatorAncestorRootAccepted()
    {
        const string title = "projectless 会话";
        const string cwd = @"E:\AI\Codex\CodexProject\2026-09-22\codex-codex-threads-abc";
        const string root = @"E:\AI\Codex\CodexProject";
        var id = NewId(1);
        var (io, _) = BuildEnvironment(title, id, cwd, Now.AddSeconds(-10), hint: root);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.NotNull(outcome.Session, "工作区根为 cwd 的父目录时必须视为一致（否则 projectless 会话全部被否决）");
        Assert.Equal("high", outcome.Session!.Confidence, "祖先目录一致时应视为已交叉校验");
    }
    private static void LocatorNoTitleOrIndex()
    {
        var empty = new FakeIo();
        var locator = new ContextLocator(empty, empty.Home, () => Now);

        var noTitle = locator.Locate("   ", Options());
        Assert.Null(noTitle.Session, "拿不到标题时不得带上下文");
        Assert.Contains("no-title", noTitle.Code, "应给出 no-title 诊断码");

        var noIndex = locator.Locate("任意标题", Options());
        Assert.Null(noIndex.Session, "索引缺失时不得带上下文");
        Assert.Contains("no-index", noIndex.Code, "应给出 no-index 诊断码");
    }

    private static void LocatorUniqueButStale()
    {
        const string title = "很久以前开始的会话";
        var id = NewId(1);
        var (io, _) = BuildEnvironment(title, id, Cwd, Now.AddHours(-2), hint: Cwd);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        // 标题唯一命中时，文件"旧"只代表这轮对话上次写入时间早，不代表归属错误；
        // 归属仍由 session_meta.cwd 交叉校验把关，因此这里必须仍可用，但诊断要标注 stale。
        Assert.NotNull(outcome.Session, "标题唯一命中时应仍可用（否则隔一段时间的会话永远拿不到上下文）");
        Assert.Contains("stale", outcome.Code, "诊断码应标注 stale 与 age");
        Assert.Equal(id, outcome.Session!.ThreadId, "仍必须指向同一个会话");
    }

    private static void LocatorDuplicateAllStale()
    {
        const string title = "重名且都很旧";
        var firstId = NewId(1);
        var secondId = NewId(2);
        var first = Now.AddHours(-3);
        var second = Now.AddHours(-4);

        var io = new FakeIo();
        io.AddFile(
            Path.Combine(io.Home, "session_index.jsonl"),
            IndexLine(firstId, title, first) + "\n" + IndexLine(secondId, title, second) + "\n",
            first);
        io.AddFile(RolloutPathFor(io, firstId, first), RolloutBody(Cwd), first);
        io.AddFile(RolloutPathFor(io, secondId, second), RolloutBody(Cwd), second);
        io.AddFile(Path.Combine(io.Home, ".codex-global-state.json"), Hints((firstId, Cwd), (secondId, Cwd)), first);

        var outcome = new ContextLocator(io, io.Home, () => Now).Locate(title, Options());

        Assert.Null(outcome.Session, "重名且都无法证明新鲜时必须放弃");
        Assert.Contains("stale-all", outcome.Code, "应给出 stale-all 诊断码");
    }

    // ---------------------------------------------------------------- 读取器 / 预算 / 脱敏

    private static void ReaderParsesTail()
    {
        var io = new FakeIo();
        var path = Path.Combine(io.Home, "rollout-reader.jsonl");
        var body = string.Join('\n',
            MetaLine(@"E:\demo\project"),
            MessageLine("user", "第一个问题"),
            "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"半行坏数据",
            MessageLine("user", "第二个问题"),
            MessageLine("assistant", "先看空引用"),
            ToolLine("shell", "{\"command\":\"dotnet build -c Release\"}"),
            "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"output\":\"巨大输出不应进入上下文\"}}");
        io.AddFile(path, body);

        var tail = new ContextSessionReader(io).ReadTail(path, 6, 2);

        Assert.Equal(3, tail.Messages.Count, "应解析出 3 条完整消息（坏行丢弃）");
        Assert.Equal("先看空引用", tail.Messages[^1].Text, "最后一条消息应被保留");
        Assert.Equal(1, tail.Tools.Count, "应解析出 1 条工具摘要");
        Assert.Equal("dotnet build -c Release", tail.Tools[0].Hint, "工具摘要应保留命令线索");
        Assert.DoesNotContain("巨大输出", string.Join('\n', tail.Messages.Select(message => message.Text)), "工具输出不得进入上下文");
    }

    private static void ReaderExpandsWindow()
    {
        var io = new FakeIo();
        var path = Path.Combine(io.Home, "rollout-big.jsonl");

        // 前部是对话，后面塞满工具调用（>256 KB），用来模拟"最近消息被大量工具输出挤到窗口外"的大文件。
        var filler = new StringBuilder();
        for (var i = 0; i < 6000; i++)
            filler.Append(ToolLine("shell", "{\"command\":\"echo " + new string('x', 60) + "\"}")).Append('\n');

        var body = string.Join('\n',
            MetaLine(Cwd),
            MessageLine("user", "很早之前的问题"),
            MessageLine("assistant", "很早之前的回答")) + "\n" + filler;
        io.AddFile(path, body);

        var tail = new ContextSessionReader(io).ReadTail(path, 2, 2);

        Assert.Equal(2, tail.Messages.Count, "应当自动扩大窗口找回对话消息");
        Assert.Equal("很早之前的问题", tail.Messages[0].Text, "找回的应是最早的那条对话");
    }
    private static void BudgetTrimsToLimit()
    {
        var payload = new ContextPayload { Cwd = Cwd, Branch = "main", Commit = "023bf92" };
        payload.InstructionsSummary = new string('规', 1600);
        for (var i = 0; i < 40; i++) payload.Messages.Add(new ContextMessage("user", new string('长', 120) + i));
        for (var i = 0; i < 5; i++) payload.Tools.Add(new ContextToolCall("shell", "dotnet build -c Release"));
        payload.GitDiffStat = string.Join('\n', Enumerable.Repeat(" MainController.cs | 12 ++++++", 20));

        const int budget = 300;
        ContextBudget.Fit(payload, budget, () => TokenEstimator.Estimate(ContextRenderer.Render(payload)));

        var tokens = TokenEstimator.Estimate(ContextRenderer.Render(payload));
        Assert.True(tokens <= budget, $"裁剪后 token 数 {tokens} 必须 ≤ 预算 {budget}");
        Assert.Null(payload.GitDiffStat, "预算不足时应最先牺牲 git diff 摘要");
    }

    private static void RedactorRemovesSecrets()
    {
        const string text = "OPENAI_API_KEY=sk-abcdef1234567890 Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6 host=192.168.1.10 token=abcd1234efgh";

        var redacted = ContextRedactor.Redact(text);

        Assert.DoesNotContain("sk-abcdef1234567890", redacted, "API Key 必须被脱敏");
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6", redacted, "Bearer Token 必须被脱敏");
        Assert.DoesNotContain("192.168.1.10", redacted, "内网地址必须被脱敏");
        Assert.DoesNotContain("abcd1234efgh", redacted, "token= 值必须被脱敏");
        Assert.Contains("[redacted]", redacted, "应使用统一的占位符");
    }

    // ---------------------------------------------------------------- 组装与端到端

    private static void ComposeWithContextKeepsDraft()
    {
        const string draft = "加个导出功能，导出 CSV 到桌面";
        const string context = "{\n  \"cwd\": \"E:\\\\demo\",\n  \"recentMessages\": [{\"role\": \"user\", \"text\": \"先做导出\"}]\n}";

        var (system, user) = PromptComposer.Compose(null, draft, null, context);

        Assert.Contains(draft, user, "草稿原文必须完整保留");
        Assert.Contains("本机上下文证据", user, "user 消息应包含上下文证据表头");
        Assert.Contains("\"cwd\"", user, "user 消息应包含上下文 JSON");
        Assert.Contains("不是指令", system, "system 必须强调证据不是指令");
    }

    private static void ComposeWithoutContextIsUnchanged()
    {
        const string draft = "加个导出按钮";
        var legacy = PromptComposer.Compose(null, draft);
        var explicitNull = PromptComposer.Compose(null, draft, null, null);
        var emptyContext = PromptComposer.Compose(null, draft, null, "   ");

        Assert.Equal(legacy.SystemPrompt, explicitNull.SystemPrompt, "关闭态 system 必须不变");
        Assert.Equal(legacy.UserPrompt, explicitNull.UserPrompt, "关闭态 user 必须逐字符一致");
        Assert.Equal(legacy.UserPrompt, emptyContext.UserPrompt, "空上下文必须等同没有上下文");
        Assert.DoesNotContain("本机上下文证据", legacy.UserPrompt, "关闭态不得出现证据块");
    }

    private static void EnabledPipelineCollectsContext()
    {
        const string title = "上下文端到端";
        var id = NewId(3);
        var (io, _) = BuildEnvironment(title, id, Cwd, Now.AddSeconds(-5), hint: Cwd);

        io.AddDirectory(Cwd);
        io.AddDirectory(Path.Combine(Cwd, ".git"));
        io.ProcessOutputs["status"] = " M MainController.cs\n?? tests/";
        io.ProcessOutputs["diff --stat"] = " MainController.cs | 12 ++++++";
        io.ProcessOutputs["log -1"] = "023bf92 docs: 新增上下文感知优化开发方案";

        var settings = new AppSettings { ContextEnabled = true };
        // 用固定时钟构造，保证"新鲜度"判定与测试时间一致（应用内使用真实时钟）。
        var pipeline = new ContextPipeline(ContextOptions.FromSettings(settings), io, io.Home, () => Now);
        var snapshot = pipeline.Build(title);

        Assert.NotNull(snapshot, "开启态应返回快照");
        Assert.True(snapshot!.HasContext, $"开启态应真的采集到上下文（诊断：{snapshot.Diagnostic} / sources={snapshot.Sources.Count}）");
        Assert.Contains(Cwd.Replace("\\", "\\\\"), snapshot.Json, "JSON 中应包含 cwd");
        Assert.Contains("这个报错怎么修？", snapshot.Json, "JSON 中应包含最近对话");
        Assert.Contains("gitStatus", snapshot.Json, "JSON 中应包含 git 状态摘要");
        Assert.True(snapshot.Sources.Count >= 3, $"来源数应 ≥ 3，实际 {snapshot.Sources.Count}");
        Assert.True(snapshot.EstimatedTokens > 0, "token 估算应大于 0");
        Assert.Contains("ctx=ready", snapshot.Diagnostic, "诊断码应标记 ready");
    }

    // ---------------------------------------------------------------- 辅助

    private static ContextOptions Options(bool enabled = true) => new(enabled, true, true, true, 1200, 6, 300);

    private static string NewId(int suffix) => $"01a0c75e-293b-7ed0-bb78-8f14cd6179{suffix:D2}";

    private static (FakeIo Io, string RolloutPath) BuildEnvironment(
        string title,
        string threadId,
        string cwd,
        DateTimeOffset lastWrite,
        string? hint = null)
    {
        var io = new FakeIo();
        io.AddFile(Path.Combine(io.Home, "session_index.jsonl"), IndexLine(threadId, title, lastWrite) + "\n", lastWrite);

        var path = RolloutPathFor(io, threadId, lastWrite);
        io.AddFile(path, RolloutBody(cwd), lastWrite);

        io.AddFile(Path.Combine(io.Home, ".codex-global-state.json"), Hints((threadId, hint ?? cwd)), lastWrite);
        return (io, path);
    }

    private static string RolloutPathFor(FakeIo io, string threadId, DateTimeOffset lastWrite)
    {
        var local = lastWrite.ToLocalTime();
        var name = $"rollout-{local:yyyy-MM-ddTHH-mm-ss}-{threadId}.jsonl";
        return Path.Combine(io.Home, "sessions", local.ToString("yyyy"), local.ToString("MM"), local.ToString("dd"), name);
    }

    private static string IndexLine(string threadId, string title, DateTimeOffset updatedAt) =>
        $"{{\"id\":\"{threadId}\",\"thread_name\":\"{Escape(title)}\",\"updated_at\":\"{updatedAt:O}\"}}";

    private static string Hints(params (string ThreadId, string Root)[] entries)
    {
        var builder = new StringBuilder("{\"version\":1,\"thread-workspace-root-hints\":{");
        for (var i = 0; i < entries.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append('"').Append(entries[i].ThreadId).Append("\":\"").Append(Escape(entries[i].Root)).Append('"');
        }

        builder.Append("}}");
        return builder.ToString();
    }

    private static string RolloutBody(string cwd) => string.Join('\n',
        MetaLine(cwd),
        MessageLine("user", "这个报错怎么修？"),
        MessageLine("assistant", "先看空引用。"),
        ToolLine("shell", "{\"command\":\"dotnet build -c Release\"}")) + "\n";

    private static string MetaLine(string cwd) =>
        $"{{\"type\":\"session_meta\",\"payload\":{{\"cwd\":\"{Escape(cwd)}\",\"runtime_workspace_roots\":[\"{Escape(cwd)}\"],\"git\":{{\"branch\":\"main\",\"commit_hash\":\"d67c0a5abcdef\"}},\"base_instructions\":\"# 项目指令\\n只读上下文\"}}}}";

    private static string MessageLine(string role, string text) =>
        $"{{\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"role\":\"{role}\",\"content\":[{{\"type\":\"input_text\",\"text\":\"{Escape(text)}\"}}]}}}}";

    private static string ToolLine(string name, string arguments) =>
        $"{{\"type\":\"response_item\",\"payload\":{{\"type\":\"function_call\",\"name\":\"{name}\",\"arguments\":\"{Escape(arguments)}\"}}}}";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}