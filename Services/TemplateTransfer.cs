using System.Text.Encodings.Web;
using System.Text.Json;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 模板导入导出：{ "version": 1, "templates": [ ... ] }。
/// 导入的模板一律视为自定义模板，Id 冲突时由调用方改名。
/// </summary>
internal static class TemplateTransfer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal sealed record Bundle(int Version, List<OptimizationTemplate> Templates);

    internal static string Export(OptimizationTemplate template) =>
        JsonSerializer.Serialize(new Bundle(1, new List<OptimizationTemplate> { template }), Options);

    internal static IReadOnlyList<OptimizationTemplate> Import(string json)
    {
        Bundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<Bundle>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("JSON 解析失败：" + ex.Message);
        }

        if (bundle?.Templates is null || bundle.Templates.Count == 0)
            throw new InvalidOperationException("文件里没有模板。");

        foreach (var template in bundle.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id))
                template.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
            template.IsBuiltin = false;
            if (string.IsNullOrWhiteSpace(template.UserTemplate))
                template.UserTemplate = PromptComposer.DefaultUserTemplate;
            if (string.IsNullOrWhiteSpace(template.SystemPrompt))
                template.SystemPrompt = AppSettings.DefaultOptimizationPrompt;
        }

        return bundle.Templates;
    }
}