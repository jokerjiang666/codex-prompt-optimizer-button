using System.Text;
using System.Text.Json;

namespace CodexInputEnhancer.Services;

internal sealed record SessionTail(
    IReadOnlyList<ContextMessage> Messages,
    IReadOnlyList<ContextToolCall> Tools,
    bool DroppedPartialLine);

/// <summary>
/// rollout 尾部增量读取：只读「上次 offset 之后」的新字节，解析成消息 / 工具摘要两类环形缓冲。
/// 消息与工具分开计数，避免大量工具调用把对话挤出缓冲；
/// 起始窗口 256 KB，若凑不齐所需消息条数再扩大到 2 MB 重读一次。
/// 文件被轮转（变短）时自动重置；半行/坏行一律丢弃，绝不抛异常。
/// </summary>
internal sealed class ContextSessionReader
{
    private const int MaxTailBytes = 256 * 1024;
    private const int ExpandedTailBytes = 2 * 1024 * 1024;
    private const int MaxMessagesKept = 40;
    private const int MaxToolsKept = 20;
    private const int MaxMessageChars = 400;
    private const int MaxToolHintChars = 200;

    private readonly IContextIo _io;
    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);

    internal ContextSessionReader(IContextIo io)
    {
        _io = io;
    }

    internal SessionTail ReadTail(string path, int messageLimit, int toolLimit)
    {
        var state = Refresh(path, MaxTailBytes);

        // 大文件里"最近若干条 message"可能落在尾部窗口之外（例如中间夹了大量工具输出），
        // 这时扩大一次窗口重读，保证上下文里的对话部分是真实的最近对话。
        if (messageLimit > 0 && CountMessages(state) < messageLimit)
            state = Refresh(path, ExpandedTailBytes);

        lock (state.Gate)
        {
            var messages = messageLimit <= 0
                ? []
                : state.Messages
                    .TakeLast(messageLimit)
                    .Select(item => new ContextMessage(item.Role, item.Text))
                    .ToList();

            var tools = toolLimit <= 0
                ? []
                : state.Tools
                    .TakeLast(toolLimit)
                    .Select(item => new ContextToolCall(item.Name, item.Hint))
                    .ToList();

            return new SessionTail(messages, tools, state.DroppedPartialLine);
        }
    }

    private State Refresh(string path, int windowBytes)
    {
        var state = GetState(path);

        lock (state.Gate)
        {
            var length = _io.GetLength(path);

            // 首次读取、文件被轮转变短、或需要扩大窗口时：整体重读这段窗口。
            if (state.WindowBytes < windowBytes || state.Offset <= 0 || state.Offset > length)
            {
                state.Messages.Clear();
                state.Tools.Clear();
                state.WindowBytes = windowBytes;
                state.Offset = Math.Max(0, length - windowBytes);

                var initial = _io.ReadFrom(path, state.Offset, windowBytes);
                if (initial.Reset) state.DroppedPartialLine = true;
                AppendParsed(state, initial.Lines);
                state.Offset = initial.NextOffset;
                return state;
            }

            // 窗口未变：只读 offset 之后的新增字节。
            var read = _io.ReadFrom(path, state.Offset, MaxTailBytes);
            if (read.Reset)
            {
                state.Messages.Clear();
                state.Tools.Clear();
                state.DroppedPartialLine = true;
            }

            AppendParsed(state, read.Lines);
            state.Offset = read.NextOffset;
            return state;
        }
    }

    private State GetState(string path)
    {
        lock (_states)
        {
            if (_states.TryGetValue(path, out var existing)) return existing;
            var created = new State();
            _states[path] = created;
            return created;
        }
    }

    private static void AppendParsed(State state, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            switch (Parse(line))
            {
                case MessageItem message:
                    state.Messages.Add(message);
                    while (state.Messages.Count > MaxMessagesKept) state.Messages.RemoveAt(0);
                    break;

                case ToolItem tool:
                    state.Tools.Add(tool);
                    while (state.Tools.Count > MaxToolsKept) state.Tools.RemoveAt(0);
                    break;
            }
        }
    }

    private static int CountMessages(State state)
    {
        lock (state.Gate)
        {
            return state.Messages.Count;
        }
    }

    private static object? Parse(string line)
    {
        if (line.Length == 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!string.Equals(GetString(root, "type"), "response_item", StringComparison.Ordinal)) return null;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;

            var kind = GetString(payload, "type");
            if (string.Equals(kind, "message", StringComparison.Ordinal)) return ParseMessage(payload);
            if (string.Equals(kind, "function_call", StringComparison.Ordinal)) return ParseTool(payload);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MessageItem? ParseMessage(JsonElement payload)
    {
        var role = GetString(payload, "role") ?? "unknown";
        if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;

        var builder = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;
            var text = GetString(part, "text");
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(text);
        }

        var cleaned = ContextRedactor.Truncate(ContextRedactor.Redact(builder.ToString()), MaxMessageChars);
        return cleaned.Length == 0 ? null : new MessageItem(role, cleaned);
    }

    private static ToolItem? ParseTool(JsonElement payload)
    {
        var name = GetString(payload, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var hint = ContextRedactor.Truncate(ContextRedactor.Redact(BuildToolHint(payload)), MaxToolHintChars);
        return new ToolItem(name!, hint);
    }

    /// <summary>工具参数里只留"命令首行 / 路径 / 查询词"这类线索，不留完整输出。</summary>
    private static string BuildToolHint(JsonElement payload)
    {
        var arguments = payload.TryGetProperty("arguments", out var args)
            ? args.ValueKind == JsonValueKind.String ? args.GetString() : args.GetRawText()
            : null;
        if (string.IsNullOrWhiteSpace(arguments)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(arguments!);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "command", "cmd", "path", "file_path", "file", "pattern", "query", "url" })
                {
                    var value = GetString(doc.RootElement, key);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var firstLine = value.Split('\n')[0].Trim();
                    if (firstLine.Length > 0) return firstLine;
                }
            }
        }
        catch (JsonException)
        {
            // 参数不是 JSON：退化为原始文本首行。
        }

        return arguments!.Split('\n')[0].Trim();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record MessageItem(string Role, string Text);

    private sealed record ToolItem(string Name, string Hint);

    private sealed class State
    {
        internal object Gate { get; } = new();

        internal List<MessageItem> Messages { get; } = [];

        internal List<ToolItem> Tools { get; } = [];

        internal long Offset { get; set; }

        internal int WindowBytes { get; set; }

        internal bool DroppedPartialLine { get; set; }
    }
}