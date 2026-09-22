using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CodexInputEnhancer.Services;

internal sealed record SessionIndexEntry(string Id, string Title, DateTimeOffset UpdatedAt);

/// <summary>
/// 读取 %CODEX_HOME%\session_index.jsonl（实测字段：id / thread_name / updated_at），
/// 这是「标题 → threadId」的高置信映射源。带 10 秒 TTL + 文件 mtime 失效缓存。
/// </summary>
internal sealed class SessionIndexStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    private readonly IContextIo _io;
    private readonly Func<DateTimeOffset> _now;
    private IReadOnlyList<SessionIndexEntry> _cache = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _cachedWriteUtc = DateTimeOffset.MinValue;

    internal SessionIndexStore(IContextIo io, Func<DateTimeOffset>? now = null)
    {
        _io = io;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal IReadOnlyList<SessionIndexEntry> Load(string indexPath)
    {
        try
        {
            if (!_io.FileExists(indexPath)) return [];

            var writeUtc = _io.GetLastWriteUtc(indexPath);

            // 文件没变，直接复用缓存（避免每轮解析全部记录）。
            if (_cache.Count > 0 && writeUtc == _cachedWriteUtc)
            {
                _cachedAt = _now();
                return _cache;
            }

            if (_cache.Count > 0 && _now() - _cachedAt < Ttl) return _cache;

            var lines = _io.ReadAllLines(indexPath);
            var entries = new List<SessionIndexEntry>(lines.Length);
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String) continue;
                    var id = idElement.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var title = root.TryGetProperty("thread_name", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                        ? titleElement.GetString() ?? string.Empty
                        : string.Empty;

                    var updatedAt = DateTimeOffset.MinValue;
                    if (root.TryGetProperty("updated_at", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.String)
                    {
                        DateTimeOffset.TryParse(
                            updatedElement.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                            out updatedAt);
                    }

                    entries.Add(new SessionIndexEntry(id!, title, updatedAt));
                }
                catch (JsonException)
                {
                    // 半行/坏行直接丢弃，绝不影响定位结果。
                }
            }

            _cache = entries;
            _cachedWriteUtc = writeUtc;
            _cachedAt = _now();
            return _cache;
        }
        catch
        {
            return _cache;
        }
    }
}