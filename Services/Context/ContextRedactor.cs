using System.Text.RegularExpressions;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 上下文脱敏与截断：密钥、Token、私钥、内网地址一律替换为占位符，
/// 保证任何被采样的文本都不会把凭据带进请求体或日志。
/// </summary>
internal static class ContextRedactor
{
    internal const string Placeholder = "[redacted]";

    private static readonly Regex[] Patterns =
    [
        new(@"sk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled),
        new(@"(?i)bearer\s+[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled),
        new(@"(?i)\b(?:api[_-]?key|access[_-]?token|auth[_-]?token|token|password|passwd|secret)\b\s*[:=]\s*[^\s""',;]{4,}", RegexOptions.Compiled),
        new(@"-----BEGIN[^-]{0,40}PRIVATE KEY-----[\s\S]*?-----END[^-]{0,40}PRIVATE KEY-----", RegexOptions.Compiled),
        new(@"\b(?:10|192\.168|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled),
        new(@"\b(?:ghp|gho|github_pat|xox[baprs])_[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled)
    ];

    internal static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = text;
        foreach (var pattern in Patterns)
            result = pattern.Replace(result, Placeholder);
        return result;
    }

    /// <summary>归一化换行 + 去首尾空白 + 按字符数截断（超出加省略号）。</summary>
    internal static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0) return string.Empty;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length == 0) return string.Empty;
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars] + "…";
    }
}