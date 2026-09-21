using System.Text.RegularExpressions;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

/// <summary>找出模板里除 {{originalPrompt}} 之外的变量占位符。</summary>
internal static partial class PromptVariables
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_\u4e00-\u9fa5]{1,32})\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    internal static IReadOnlyList<string> FindPlaceholders(OptimizationTemplate? template)
    {
        if (template is null) return Array.Empty<string>();

        var text = (template.SystemPrompt ?? string.Empty) + "\n" + (template.UserTemplate ?? string.Empty);
        var names = new List<string>();

        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            var name = match.Groups[1].Value;
            if (string.Equals(name, "originalPrompt", StringComparison.Ordinal)) continue;
            if (names.Contains(name, StringComparer.Ordinal)) continue;
            names.Add(name);
        }

        return names;
    }
}