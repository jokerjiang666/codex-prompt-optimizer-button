namespace CodexInputEnhancer.ContextTests;

/// <summary>极简断言框架：不引入外部测试包，失败即抛异常并计入统计。</summary>
internal static class Assert
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Null(object? value, string message)
    {
        if (value is not null) throw new Exception(message);
    }

    internal static void NotNull(object? value, string message)
    {
        if (value is null) throw new Exception(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}（期望 {expected}，实际 {actual}）");
    }

    internal static void Contains(string needle, string? haystack, string message)
    {
        if (haystack is null || !haystack.Contains(needle, StringComparison.Ordinal))
            throw new Exception(message);
    }

    internal static void DoesNotContain(string needle, string? haystack, string message)
    {
        if (haystack is not null && haystack.Contains(needle, StringComparison.Ordinal))
            throw new Exception(message);
    }
}