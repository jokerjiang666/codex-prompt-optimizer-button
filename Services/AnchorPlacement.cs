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
    ///
    /// 判定方式用“相邻链”：从权限芯片右边界出发，只合并紧挨着当前右边界的同排芯片
    /// （实测芯片间距约 23px @150%）。**不能用窗口中线**来切分左右控件——
    /// 打开侧边文件栏后输入框整体左移，右侧的模型芯片会落进窗口左半边，
    /// 锚点就会被带着穿过整个输入框，悬浮按钮随之压到听写/语音按钮上。
    /// </summary>
    internal static Rect ExtendAcrossLeftCluster(Rect anchor, IEnumerable<Rect> candidates)
    {
        if (anchor.IsEmpty) return anchor;

        var maxGap = Math.Max(16, anchor.Height * 0.75);
        var right = anchor.Right;
        var rowTop = anchor.Top;
        var rowBottom = anchor.Bottom;

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var bounds in candidates)
            {
                if (bounds.IsEmpty) continue;

                // 必须与锚点处于同一行，避免把上方内容区或其它行的控件算进来。
                if (bounds.Bottom < rowTop || bounds.Top > rowBottom) continue;
                if (bounds.Height > anchor.Height + 10) continue;

                // 只接受紧挨着当前右边界的芯片，远处控件（模型/听写/发送）自然被排除。
                var gap = bounds.Left - right;
                if (gap < -8 || gap > maxGap) continue;
                if (bounds.Right <= right) continue;

                right = bounds.Right;
                changed = true;
            }
        }

        return right > anchor.Right
            ? new Rect(anchor.Left, anchor.Top, right - anchor.Left, anchor.Height)
            : anchor;
    }
}