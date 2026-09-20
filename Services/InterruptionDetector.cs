namespace CodexInputEnhancer.Services;

/// <summary>
/// 中断/报错的文本判定（纯函数，便于单独验证）。
/// 只在「生成中 → 空闲」的那一刻扫描会话尾部，命中即认为需要自动继续。
/// </summary>
internal static class InterruptionDetector
{
    // 命中即视为中断。不要加入过宽的词（例如单独出现的 "timeout"、"中断"），避免把正文误判成错误。
    private static readonly string[] ErrorMarkers =
    {
        "exceeded retry limit",
        "retry limit",
        "429 too many requests",
        "too many requests",
        "rate limit",
        "network error",
        "connection error",
        "connection reset",
        "stream error",
        "request timed out",
        "timed out",
        "failed to fetch",
        "socket hang up",
        "econnreset",
        "重试次数已用完",
        "超出重试上限",
        "重试上限",
        "请求过于频繁",
        "网络错误",
        "网络异常",
        "连接中断",
        "连接失败",
        "请求失败",
        "响应中断",
        "服务异常",
        "服务器错误"
    };

    /// <summary>判断文本是否为中断/报错，并给出用于日志与幂等的签名。</summary>
    internal static bool IsInterruption(string? text, out string signature)
    {
        signature = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var marker in ErrorMarkers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            signature = BuildSignature(text, index);
            return true;
        }

        return false;
    }

    private static string BuildSignature(string text, int markerIndex)
    {
        var start = Math.Max(0, markerIndex - 24);
        var length = Math.Min(96, text.Length - start);
        var slice = text.Substring(start, length);

        var buffer = new char[Math.Min(80, slice.Length)];
        var count = 0;
        foreach (var c in slice)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (count == buffer.Length) break;
            buffer[count++] = c;
        }

        return new string(buffer, 0, count);
    }
}