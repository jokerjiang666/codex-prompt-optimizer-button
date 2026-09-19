using System.Windows;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 悬浮按钮锚点的纯几何计算，便于单独验证。
/// </summary>
internal static class AnchorPlacement
{
    /// <summary>
    /// 权限芯片右侧还可能并排出现“目标”等同排芯片。锚点必须覆盖整段左侧工具组，
    /// 否则悬浮按钮会压在相邻芯片（例如“目标”）上。
    /// 右侧的模型/听写/发送等控件位于窗口右半边，用中线切分即可排除。
    /// </summary>
    internal static Rect ExtendAcrossLeftCluster(Rect anchor, Rect hostBounds, IEnumerable<Rect> candidates)
    {
        if (anchor.IsEmpty) return anchor;

        var clusterLimit = hostBounds.Left + hostBounds.Width * 0.5;
        var right = anchor.Right;

        foreach (var bounds in candidates)
        {
            if (bounds.IsEmpty) continue;
            if (bounds.Left >= clusterLimit) continue;

            // 必须与锚点处于同一行，避免把上方内容区的控件算进来。
            if (bounds.Bottom < anchor.Top || bounds.Top > anchor.Bottom) continue;
            if (bounds.Height > anchor.Height + 10) continue;

            if (bounds.Right > right) right = bounds.Right;
        }

        return right > anchor.Right
            ? new Rect(anchor.Left, anchor.Top, right - anchor.Left, anchor.Height)
            : anchor;
    }
}
