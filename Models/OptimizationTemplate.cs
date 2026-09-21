using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexInputEnhancer.Models;

/// <summary>
/// 一条优化模板：system 负责角色与规则，user 模板负责「原文占位 + 输出要求」。
/// 实现 INotifyPropertyChanged，便于设置面板直接绑定编辑。
/// </summary>
public sealed class OptimizationTemplate : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _category = "通用";
    private string _systemPrompt = string.Empty;
    private string _userTemplate = string.Empty;
    private string _language = "zh";

    public string Id { get; set; } = string.Empty;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    public string Category
    {
        get => _category;
        set => Set(ref _category, value);
    }

    /// <summary>角色、目标、约束、输出要求。</summary>
    public string SystemPrompt
    {
        get => _systemPrompt;
        set => Set(ref _systemPrompt, value);
    }

    /// <summary>必须包含 {{originalPrompt}}；缺失时由 PromptComposer 自动补骨架。</summary>
    public string UserTemplate
    {
        get => _userTemplate;
        set => Set(ref _userTemplate, value);
    }

    public bool IsBuiltin { get; set; }

    public string Language
    {
        get => _language;
        set => Set(ref _language, value);
    }

    public int Version { get; set; } = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}