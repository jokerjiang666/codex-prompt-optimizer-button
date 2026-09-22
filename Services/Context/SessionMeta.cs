using System.IO;
using System.Text.Json;

namespace CodexInputEnhancer.Services;

/// <summary>rollout 首行 session_meta 的关键字段（实测字段名）。</summary>
internal sealed record SessionMeta(
    string Cwd,
    IReadOnlyList<string> WorkspaceRoots,
    string? Branch,
    string? CommitHash,
    string? Instructions);

internal static class SessionMetaParser
{
    internal static SessionMeta? TryParse(string? firstLine)
    {
        if (string.IsNullOrWhiteSpace(firstLine)) return null;

        try
        {
            using var doc = JsonDocument.Parse(firstLine);
            var root = doc.RootElement;
            if (!string.Equals(GetString(root, "type"), "session_meta", StringComparison.Ordinal)) return null;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;

            var cwd = GetString(payload, "cwd");
            if (string.IsNullOrWhiteSpace(cwd)) return null;

            var roots = new List<string>();
            if (payload.TryGetProperty("runtime_workspace_roots", out var rootsElement) && rootsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rootsElement.EnumerateArray())
                {
                    var root_ = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(root_)) roots.Add(root_!);
                }
            }

            string? branch = null;
            string? commit = null;
            if (payload.TryGetProperty("git", out var git) && git.ValueKind == JsonValueKind.Object)
            {
                branch = GetString(git, "branch");
                commit = GetString(git, "commit_hash");
            }

            return new SessionMeta(cwd!, roots, branch, commit, GetString(payload, "base_instructions"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}