namespace CodexInputEnhancer.Models;

public sealed class AppSettings
{
    public const string DefaultOptimizationPrompt = """
        你是 Codex 输入优化器。你的唯一任务是把用户草稿改写成更清晰、可直接执行的输入；不要执行任务、不要回答问题、不要调用工具。

        输出要求：
        - 只输出优化后的完整输入内容，不要输出解释、前言、后记、标题、引号、代码块包裹或额外标注。
        - 保留原文语言、语气、意图、事实、数字、路径、命令、链接、文件名、专有名词和技术术语；不要新增、删改或猜测用户没有提供的信息。
        - 保留原文的格式和必要结构；如果原文已经足够清晰，只做最小必要润色。

        优化规则：
        - 让目标、背景（如有）、约束、执行动作和验收条件更明确。
        - 对开发任务，优先写成可以直接交给 Codex 执行的自然中文；代码、命令、路径、文件名和专有名词保持原样。
        - 原文缺少的信息不要补造；无法确定时保留原表述，不要替用户做决定。
        - 不要为了显得详细而机械扩写，不要改变原意的范围、优先级或强度。

        在内部完成自检：是否保留原意、是否遗漏约束、是否只输出了优化后的文本。不要输出自检过程。
        """;

    public string Provider { get; set; } = "codex-cli";
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.6-sol";
    public string ReasoningEffort { get; set; } = "low";
    public int TimeoutSeconds { get; set; } = 60;
    public bool HistoryEnabled { get; set; } = true;
    /// <summary>检测到中断/报错时自动发送继续内容（默认关闭）。</summary>
    public bool AutoContinueEnabled { get; set; }

    /// <summary>继续内容：悬浮窗继续按钮与自动继续共用。</summary>
    public string AutoContinueText { get; set; } = "继续";

    /// <summary>两次自动继续之间的最小间隔（秒）。</summary>
    public int AutoContinueMinIntervalSeconds { get; set; } = 10;

    /// <summary>是否在悬浮窗显示「继续」按钮。</summary>
    public bool ShowContinueButton { get; set; } = true;
    public string OptimizationPrompt { get; set; } = DefaultOptimizationPrompt;
    /// <summary>优化模板库（内置 + 自定义）。</summary>
    public List<OptimizationTemplate> Templates { get; set; } = new();

    /// <summary>当前选中的模板 Id。</summary>
    public string ActiveTemplateId { get; set; } = "builtin-basic";

    /// <summary>深度优化（多轮迭代）。</summary>
    public bool DeepOptimizeEnabled { get; set; }

    public int DeepOptimizeRounds { get; set; } = 2;

    /// <summary>替换草稿前先弹优化结果预览。</summary>
    public bool PreviewBeforeApply { get; set; } = true;

    /// <summary>上下文增强总开关。默认关；关闭时不做任何上下文采集。</summary>
    public bool ContextEnabled { get; set; }

    /// <summary>携带最近对话尾部（仅在总开关开启时生效）。</summary>
    public bool ContextThreadTailEnabled { get; set; } = true;

    /// <summary>携带工作区 git 摘要（仅在总开关开启时生效）。</summary>
    public bool ContextGitEnabled { get; set; } = true;

    /// <summary>携带会话元信息：cwd / 分支 / 项目指令摘要。</summary>
    public bool ContextWorkspaceMetaEnabled { get; set; } = true;

    /// <summary>上下文 token 预算上限（估算值）。</summary>
    public int ContextTokenBudget { get; set; } = 1200;

    /// <summary>进入上下文的最近对话条数上限。</summary>
    public int ContextMessageLimit { get; set; } = 6;

    /// <summary>会话文件新鲜度上限（秒）；超过则认为不是当前会话。</summary>
    public int ContextStaleSeconds { get; set; } = 300;

    /// <summary>优化结果预览里显示本次上下文来源。</summary>
    public bool ContextShowInPreview { get; set; } = true;
    /// <summary>模板语言：zh / en。</summary>
    public string TemplateLanguage { get; set; } = "zh";

    /// <summary>配置面板风格：A（外壳）/ D（模板页）等。</summary>
    public string SettingsStyle { get; set; } = "A";
}
