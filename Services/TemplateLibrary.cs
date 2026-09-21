using System.Text.Encodings.Web;
using System.Text.Json;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 内置模板定义、旧配置迁移与基础校验。
/// 模板文本为本项目自研，未复制第三方（AGPL）内容。
/// </summary>
internal static class TemplateLibrary
{
    internal const string LegacyTemplateId = "custom-legacy";

    internal static IReadOnlyList<OptimizationTemplate> Builtins { get; } = new[]
    {
        Builtin("builtin-basic", "基础优化", "消除模糊表达、补充关键信息，保持原意", "通用",
            """
            你是用户输入基础优化器。只改写输入，不执行任务、不回答问题、不调用工具。
            输出要求：只输出优化后的完整输入；保留原语言、事实、数字、路径、命令、文件名、链接与专有名词；
            不新增用户未提供的信息；原文已经足够清晰时只做最小必要润色。
            """),
        Builtin("builtin-task", "需求拆解", "把一句话需求拆成目标 / 范围 / 约束 / 验收", "开发",
            """
            你是需求拆解器。把用户的一句话需求整理成可直接执行的开发任务。
            输出结构：目标、范围（含不要做什么）、约束条件、验收标准。
            保持用户原意，不补造业务事实、路径或账号；缺少的必要信息写成「待确认」。
            """),
        Builtin("builtin-bug", "Bug 复现", "整理成现象 / 环境 / 复现步骤 / 期望与实际", "开发",
            """
            你是缺陷描述整理器。把用户描述整理成一份可复现的缺陷报告。
            输出结构：现象、影响范围、环境信息、复现步骤、期望结果、实际结果、日志或线索。
            只使用用户提供的信息，缺失项写「待补充」，不要编造。
            """),
        Builtin("builtin-review", "代码审查", "明确审查范围、关注点与输出格式", "开发",
            """
            你是代码审查需求整理器。把用户意图整理成一次可执行的代码审查请求。
            输出结构：审查范围（文件或模块）、关注点（正确性 / 性能 / 安全 / 可维护性）、
            输出格式（按严重级别列出问题与建议）、明确不需要做的部分。不要替用户做技术选型决策。
            """),
        Builtin("builtin-refactor", "重构需求", "说明现状、目标结构、兼容边界与验证方式", "开发",
            """
            你是重构需求整理器。把用户的改造意图整理成可执行的重构任务。
            输出结构：现状与问题、目标结构、必须保持的对外行为、明确禁止改动的边界、验证方式。
            保持范围最小化，不擅自扩大重构范围。
            """),
        Builtin("builtin-doc", "文档 / 说明", "面向读者组织要点与交付格式", "写作",
            """
            你是文档需求整理器。把用户意图整理成一份写作任务。
            输出结构：读者对象、文档类型、必须覆盖的要点、篇幅与语气、交付格式（Markdown / 表格等）。
            不要替用户补充未提供的结论或数据。
            """),
        Builtin("builtin-image", "图像提示词", "主体 / 构图 / 光线 / 风格 / 画幅", "图像",
            """
            你是图像提示词整理器。把用户的想法整理成可直接用于文生图的提示词。
            尽量覆盖：主体与外观、构图与视角、光线与氛围、风格与材质、画幅与分辨率、需要避免的元素。
            保留用户给出的专有名词与品牌词，不新增未提供的设定。
            """),
        Builtin("builtin-video", "视频提示词", "镜头 / 动作 / 时长 / 运镜 / 风格", "视频",
            """
            你是视频提示词整理器。把用户的想法整理成可直接用于图生视频或文生视频的提示词。
            尽量覆盖：镜头与景别、主体动作与表演、环境与光线、运镜方式、时长与节奏、风格参考。
            一句话只保留一个主要动作，保持时序清晰。
            """)
    };

    private static OptimizationTemplate Builtin(string id, string name, string description, string category, string system) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Category = category,
        SystemPrompt = system,
        UserTemplate = PromptComposer.DefaultUserTemplate,
        IsBuiltin = true,
        Language = "zh",
        Version = 1
    };

    /// <summary>补齐内置模板、迁移旧配置，返回是否有改动。</summary>
    internal static bool Ensure(AppSettings settings)
    {
        var changed = false;

        settings.Templates ??= new List<OptimizationTemplate>();
        if (settings.Templates.Count == 0)
        {
            settings.Templates.AddRange(Builtins.Select(Clone));

            // 只有用户真的改过旧提示词才迁移成自定义模板（新装用户直接用「基础优化」）。
            var customized = !string.IsNullOrWhiteSpace(settings.OptimizationPrompt)
                             && !string.Equals(settings.OptimizationPrompt.Trim(), AppSettings.DefaultOptimizationPrompt.Trim(), StringComparison.Ordinal);

            if (customized)
            {
                settings.Templates.Add(new OptimizationTemplate
                {
                    Id = LegacyTemplateId,
                    Name = "旧版自定义提示词",
                    Description = "从旧版本的优化提示词迁移而来",
                    Category = "自定义",
                    SystemPrompt = settings.OptimizationPrompt.Trim(),
                    UserTemplate = PromptComposer.DefaultUserTemplate,
                    IsBuiltin = false,
                    Language = "zh",
                    Version = 1
                });
            }

            settings.ActiveTemplateId = customized ? LegacyTemplateId : Builtins[0].Id;
            changed = true;
        }
        else
        {
            foreach (var builtin in Builtins)
            {
                if (settings.Templates.Any(t => string.Equals(t.Id, builtin.Id, StringComparison.Ordinal))) continue;
                settings.Templates.Add(Clone(builtin));
                changed = true;
            }
        }

        if (Resolve(settings) is null)
        {
            settings.ActiveTemplateId = Builtins[0].Id;
            changed = true;
        }

        settings.TemplateLanguage = string.Equals(settings.TemplateLanguage, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        settings.DeepOptimizeRounds = Math.Clamp(settings.DeepOptimizeRounds, 1, 3);

        return changed;
    }

    internal static OptimizationTemplate? Resolve(AppSettings settings)
    {
        var active = settings.Templates?.FirstOrDefault(t => string.Equals(t.Id, settings.ActiveTemplateId, StringComparison.Ordinal));
        return active ?? settings.Templates?.FirstOrDefault();
    }

    internal static OptimizationTemplate? Find(AppSettings settings, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : settings.Templates?.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    internal static OptimizationTemplate Clone(OptimizationTemplate source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        Category = source.Category,
        SystemPrompt = source.SystemPrompt,
        UserTemplate = source.UserTemplate,
        IsBuiltin = source.IsBuiltin,
        Language = source.Language,
        Version = source.Version
    };
    // ---------- 导入 / 导出 / 自定义模板 ----------

    internal static string ExportJson(IEnumerable<OptimizationTemplate> templates)
    {
        var payload = new TemplateBundle
        {
            Version = 1,
            Templates = templates.Select(Clone).ToList()
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>导入模板 JSON：支持带 version 的 bundle，也兼容裸数组。返回成功导入的条数。</summary>
    internal static int ImportJson(AppSettings settings, string json, bool overwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("模板内容为空。");

        settings.Templates ??= new List<OptimizationTemplate>();
        var imported = ParseBundle(json);
        var count = 0;

        foreach (var template in imported)
        {
            Normalize(template);
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new InvalidOperationException("模板缺少名称，无法导入。");

            var existing = settings.Templates.FirstOrDefault(t => string.Equals(t.Id, template.Id, StringComparison.Ordinal));
            if (existing is null)
            {
                template.Id = EnsureUniqueId(settings, template.Id);
                settings.Templates.Add(template);
            }
            else if (overwriteExisting && !existing.IsBuiltin)
            {
                var index = settings.Templates.IndexOf(existing);
                template.Id = existing.Id;
                settings.Templates[index] = template;
            }
            else
            {
                template.Id = EnsureUniqueId(settings, template.Id);
                settings.Templates.Add(template);
            }

            count++;
        }

        return count;
    }

    private static List<OptimizationTemplate> ParseBundle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<OptimizationTemplate>>(root.GetRawText(), JsonOptions) ?? new();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Templates", out var list))
            return JsonSerializer.Deserialize<List<OptimizationTemplate>>(list.GetRawText(), JsonOptions) ?? new();

        throw new InvalidOperationException("模板 JSON 格式不正确：缺少 Templates 数组。");
    }

    internal static OptimizationTemplate CreateCustom(AppSettings settings, string name)
    {
        settings.Templates ??= new List<OptimizationTemplate>();
        return new OptimizationTemplate
        {
            Id = EnsureUniqueId(settings, "custom-" + Guid.NewGuid().ToString("N")[..8]),
            Name = string.IsNullOrWhiteSpace(name) ? "新建模板" : name.Trim(),
            Description = "自定义模板",
            Category = "自定义",
            SystemPrompt = Builtins[0].SystemPrompt,
            UserTemplate = PromptComposer.DefaultUserTemplate,
            IsBuiltin = false,
            Language = "zh",
            Version = 1
        };
    }

    internal static void Normalize(OptimizationTemplate template)
    {
        template.Id = string.IsNullOrWhiteSpace(template.Id) ? "custom-" + Guid.NewGuid().ToString("N")[..8] : template.Id.Trim();
        template.Name = template.Name?.Trim() ?? string.Empty;
        template.Description = template.Description?.Trim() ?? string.Empty;
        template.Category = string.IsNullOrWhiteSpace(template.Category) ? "自定义" : template.Category.Trim();
        template.SystemPrompt = string.IsNullOrWhiteSpace(template.SystemPrompt)
            ? AppSettings.DefaultOptimizationPrompt
            : template.SystemPrompt.Trim();
        template.Language = string.Equals(template.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        if (template.Version < 1) template.Version = 1;

        if (string.IsNullOrWhiteSpace(template.UserTemplate)
            || !template.UserTemplate.Contains(PromptComposer.OriginalPlaceholder, StringComparison.Ordinal))
        {
            var body = string.IsNullOrWhiteSpace(template.UserTemplate)
                ? string.Empty
                : template.UserTemplate.TrimEnd() + Environment.NewLine + Environment.NewLine;
            template.UserTemplate = body + PromptComposer.DefaultUserTemplate;
        }
    }

    internal static string EnsureUniqueId(AppSettings settings, string desired)
    {
        var id = string.IsNullOrWhiteSpace(desired) ? "custom-" + Guid.NewGuid().ToString("N")[..8] : desired.Trim();
        var candidate = id;
        var suffix = 2;
        while (settings.Templates?.Any(t => string.Equals(t.Id, candidate, StringComparison.Ordinal)) == true)
        {
            candidate = id + "-" + suffix;
            suffix++;
        }
        return candidate;
    }

    private sealed class TemplateBundle
    {
        public int Version { get; set; } = 1;
        public List<OptimizationTemplate> Templates { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };
}
