using System.IO;
using System.Text.Json;

namespace CodexInputEnhancer.Services;

/// <summary>
/// .codex-global-state.json 里的 threadId → 工作区根 提示，用作 cwd 交叉校验。
/// 该文件存在大小写混用的键（实测），因此必须大小写不敏感读取；
/// 解析失败只降级为"无提示"，绝不阻断优化。
/// </summary>
internal sealed class GlobalStateHints
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly IContextIo _io;
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    internal GlobalStateHints(IContextIo io, string codexHome, Func<DateTimeOffset>? now = null)
    {
        _io = io;
        _path = Path.Combine(codexHome, ".codex-global-state.json");
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal bool LastReadFailed { get; private set; }

    internal string? TryGetWorkspaceRoot(string threadId)
    {
        EnsureLoaded();
        if (_roots.TryGetValue(threadId, out var root)) return root;
        return _outputs.TryGetValue(threadId, out var output) ? output : null;
    }

    private void EnsureLoaded()
    {
        if (_cachedAt != DateTimeOffset.MinValue && _now() - _cachedAt < Ttl) return;
        _cachedAt = _now();
        LastReadFailed = false;

        try
        {
            if (!_io.FileExists(_path)) return;
            var text = _io.ReadAllText(_path);
            using var doc = JsonDocument.Parse(text);
            ReadMap(doc.RootElement, "thread-workspace-root-hints", _roots);
            ReadMap(doc.RootElement, "thread-projectless-output-directories", _outputs);
        }
        catch
        {
            LastReadFailed = true;
        }
    }

    private static void ReadMap(JsonElement root, string name, Dictionary<string, string> target)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!TryGetPropertyIgnoreCase(root, name, out var map) || map.ValueKind != JsonValueKind.Object) return;

        foreach (var property in map.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            target[property.Name] = value!;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }
}