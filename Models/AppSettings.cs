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
    public string OptimizationPrompt { get; set; } = DefaultOptimizationPrompt;
}
