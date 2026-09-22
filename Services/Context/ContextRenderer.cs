using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexInputEnhancer.Services;

/// <summary>把结构化上下文渲染成一段 JSON 证据（紧凑、稳定、缺字段就不出现）。</summary>
internal static class ContextRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string Render(ContextPayload payload)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(payload.CapturedAt)) model["capturedAt"] = payload.CapturedAt;
        if (!string.IsNullOrWhiteSpace(payload.Cwd)) model["cwd"] = payload.Cwd;
        if (payload.WorkspaceRoots.Count > 0) model["workspaceRoots"] = payload.WorkspaceRoots.ToArray();

        if (!string.IsNullOrWhiteSpace(payload.Branch) || !string.IsNullOrWhiteSpace(payload.Commit))
        {
            model["git"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["branch"] = string.IsNullOrWhiteSpace(payload.Branch) ? null : payload.Branch,
                ["commit"] = string.IsNullOrWhiteSpace(payload.Commit) ? null : payload.Commit
            };
        }

        if (!string.IsNullOrWhiteSpace(payload.InstructionsSummary)) model["instructions"] = payload.InstructionsSummary;

        if (payload.Messages.Count > 0)
        {
            model["recentMessages"] = payload.Messages
                .Select(message => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["role"] = message.Role,
                    ["text"] = message.Text
                })
                .ToArray();
        }

        if (payload.Tools.Count > 0)
        {
            model["toolSummary"] = payload.Tools
                .Select(tool => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = tool.Name,
                    ["hint"] = string.IsNullOrWhiteSpace(tool.Hint) ? null : tool.Hint
                })
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(payload.GitStatus)) model["gitStatus"] = payload.GitStatus;
        if (!string.IsNullOrWhiteSpace(payload.GitDiffStat)) model["gitDiffStat"] = payload.GitDiffStat;
        if (!string.IsNullOrWhiteSpace(payload.GitLog)) model["gitLog"] = payload.GitLog;

        return JsonSerializer.Serialize(model, Options);
    }
}