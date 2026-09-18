using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class WindowTracker
{
    public string? TryGetSessionIdentity(IntPtr windowHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            if (root is null) return null;

            var headerTitle = TryGetHeaderThreadTitle(root);
            if (!string.IsNullOrWhiteSpace(headerTitle))
                return $"{root.Current.ProcessId}:{headerTitle}";

            var documents = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));

            AutomationElement? best = null;
            string? bestName = null;
            var bestArea = 0.0;
            foreach (AutomationElement document in documents)
            {
                try
                {
                    if (document.Current.IsOffscreen) continue;
                    var name = document.Current.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var bounds = document.Current.BoundingRectangle;
                    if (bounds.IsEmpty) continue;
                    var area = bounds.Width * bounds.Height;
                    if (area <= bestArea) continue;

                    best = document;
                    bestName = name;
                    bestArea = area;
                }
                catch (ElementNotAvailableException) { }
            }

            if (best is null || string.IsNullOrWhiteSpace(bestName)) return null;
            return $"{best.Current.ProcessId}:{bestName}";
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static string? TryGetHeaderThreadTitle(AutomationElement root)
    {
        try
        {
            var rootBounds = root.Current.BoundingRectangle;
            if (rootBounds.IsEmpty) return null;

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            AutomationElement? best = null;
            var bestTop = double.MaxValue;
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    if (button.Current.IsOffscreen || !button.Current.IsEnabled) continue;
                    var name = button.Current.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var className = button.Current.ClassName ?? string.Empty;
                    if (!className.Contains("max-w-[320px]", StringComparison.OrdinalIgnoreCase)
                        || !className.Contains("truncate", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var bounds = button.Current.BoundingRectangle;
                    if (bounds.IsEmpty) continue;
                    if (bounds.Top < rootBounds.Top || bounds.Top > rootBounds.Top + 130) continue;
                    if (bounds.Left < rootBounds.Left + 380 || bounds.Left > rootBounds.Left + 1100) continue;

                    if (bounds.Top >= bestTop) continue;
                    best = button;
                    bestTop = bounds.Top;
                }
                catch (ElementNotAvailableException) { }
            }

            return best?.Current.Name?.Trim();
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public ComposerTarget? TryFindComposer()
    {
        try
        {
            foreach (var host in FindHostWindows())
            {
                var root = AutomationElement.FromHandle(host.Handle);
                if (root is null) continue;

                var processId = root.Current.ProcessId;
                var element = FindBestCandidate(root, host.Bounds, ControlType.Edit)
                              ?? FindBestCandidate(root, host.Bounds, ControlType.Document)
                              ?? FindBestGlobalCandidate(host.Bounds, ControlType.Edit, processId)
                              ?? FindBestGlobalCandidate(host.Bounds, ControlType.Document, processId);

                if (element is not null)
                {
                    var composerBounds = FindComposerBounds(element, host.Bounds);
                    var permissionAnchor = FindPermissionAnchor(root, host.Bounds)
                                           ?? FindGlobalPermissionAnchor(host.Bounds, root.Current.ProcessId);
                    return new ComposerTarget(host.Handle, element, composerBounds, permissionAnchor);
                }
            }

            return null;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public bool IsGenerationInProgress(IntPtr windowHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            if (root is null) return false;
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    if (button.Current.IsOffscreen) continue;
                    var name = button.Current.Name ?? string.Empty;
                    if (name.Contains("停止回答", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("停止生成", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("stop generating", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name.Trim(), "停止", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ElementNotAvailableException) { }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        return false;
    }

    private static IEnumerable<(IntPtr Handle, Rect Bounds)> FindHostWindows()
    {
        var processIds = new HashSet<int>();

        foreach (var processName in new[] { "Codex", "ChatGPT" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase))
                    {
                        string? executablePath = null;
                        try { executablePath = process.MainModule?.FileName; } catch { }

                        if (!string.IsNullOrWhiteSpace(executablePath)
                            && !executablePath.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    processIds.Add(process.Id);
                }
                catch (InvalidOperationException) { }
            }
        }

        var desktop = AutomationElement.RootElement;
        if (desktop is null || processIds.Count == 0)
            return Array.Empty<(IntPtr Handle, Rect Bounds)>();

        var windows = desktop.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        var hosts = new List<(IntPtr Handle, Rect Bounds)>();
        var seen = new HashSet<IntPtr>();

        foreach (AutomationElement window in windows)
        {
            try
            {
                if (!processIds.Contains(window.Current.ProcessId) || window.Current.IsOffscreen) continue;

                var handle = new IntPtr(window.Current.NativeWindowHandle);
                if (handle == IntPtr.Zero || !seen.Add(handle)) continue;

                var bounds = window.Current.BoundingRectangle;
                if (bounds.IsEmpty || bounds.Width < 500 || bounds.Height < 400) continue;

                hosts.Add((handle, bounds));
            }
            catch (ElementNotAvailableException) { }
        }

        return hosts.OrderByDescending(host => host.Bounds.Width * host.Bounds.Height);
    }

    private static AutomationElement? FindBestGlobalCandidate(Rect hostBounds, ControlType type, int processId)
    {
        var root = AutomationElement.RootElement;
        if (root is null) return null;

        var elements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));

        return RankCandidates(elements, hostBounds, processId);
    }

    private static AutomationElement? FindBestCandidate(
        AutomationElement root,
        Rect windowBounds,
        ControlType type)
    {
        var elements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));

        return RankCandidates(elements, windowBounds, null);
    }

    private static AutomationElement? RankCandidates(
        AutomationElementCollection elements,
        Rect windowBounds,
        int? processId)
    {
        AutomationElement? best = null;
        var bestScore = double.MinValue;

        foreach (AutomationElement element in elements)
        {
            try
            {
                if (!element.Current.IsEnabled || element.Current.IsOffscreen) continue;
                if (processId is not null && element.Current.ProcessId != processId.Value) continue;
                var bounds = element.Current.BoundingRectangle;
                if (bounds.IsEmpty) continue;

                var isProseMirror = (element.Current.ClassName ?? string.Empty)
                    .StartsWith("ProseMirror", StringComparison.OrdinalIgnoreCase);
                if (!isProseMirror || !SupportsWritableValue(element)) continue;
                if (bounds.Width < 8 || bounds.Height < 8 || !element.Current.IsKeyboardFocusable) continue;

                var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                if (!windowBounds.Contains(center)) continue;

                var lowerHalf = windowBounds.Top + windowBounds.Height * 0.48;
                if (bounds.Bottom < lowerHalf || bounds.Top > windowBounds.Bottom) continue;

                var score = 6200
                            + (bounds.Bottom - lowerHalf)
                            + Math.Min(bounds.Width, 1400) * 0.10
                            - Math.Max(0, bounds.Height - 420) * 0.35;
                if (score <= bestScore) continue;

                best = element;
                bestScore = score;
            }
            catch (ElementNotAvailableException) { }
        }

        return best;
    }

    private static Rect FindComposerBounds(AutomationElement editor, Rect hostBounds)
    {
        Rect best = editor.Current.BoundingRectangle;
        var walker = TreeWalker.RawViewWalker;
        var parent = walker.GetParent(editor);
        var minimumWidth = Math.Min(320, hostBounds.Width * 0.35);

        for (var depth = 0; depth < 8 && parent is not null; depth++)
        {
            try
            {
                var bounds = parent.Current.BoundingRectangle;
                if (!bounds.IsEmpty
                    && bounds.Width >= minimumWidth
                    && bounds.Height >= 34
                    && bounds.Height <= Math.Min(420, hostBounds.Height * 0.42)
                    && hostBounds.Contains(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)))
                {
                    best = bounds;
                    break;
                }

                parent = walker.GetParent(parent);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }

        return best;
    }

    private static Rect? FindPermissionAnchor(AutomationElement root, Rect hostBounds)
    {
        try
        {
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            Rect? best = null;
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    if (!button.Current.IsEnabled || button.Current.IsOffscreen) continue;
                    var name = button.Current.Name ?? string.Empty;
                    if (!name.Contains("权限", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("访问", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var bounds = button.Current.BoundingRectangle;
                    if (bounds.IsEmpty) continue;
                    var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    if (!hostBounds.Contains(center)) continue;
                    if (bounds.Top < hostBounds.Top + hostBounds.Height * 0.55) continue;

                    if (best is null || bounds.Left < best.Value.Left)
                        best = bounds;
                }
                catch (ElementNotAvailableException) { }
            }

            return best;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static Rect? FindGlobalPermissionAnchor(Rect hostBounds, int processId)
    {
        try
        {
            var desktop = AutomationElement.RootElement;
            if (desktop is null) return null;

            var buttons = desktop.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            Rect? best = null;
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    if (button.Current.ProcessId != processId
                        || !button.Current.IsEnabled
                        || button.Current.IsOffscreen)
                    {
                        continue;
                    }

                    var name = button.Current.Name ?? string.Empty;
                    if (!name.Contains("权限", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("访问", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var bounds = button.Current.BoundingRectangle;
                    if (bounds.IsEmpty) continue;
                    var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    if (!hostBounds.Contains(center)) continue;
                    if (bounds.Top < hostBounds.Top + hostBounds.Height * 0.55) continue;

                    if (best is null || bounds.Left < best.Value.Left)
                        best = bounds;
                }
                catch (ElementNotAvailableException) { }
            }

            return best;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool SupportsWritableValue(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
                   && pattern is ValuePattern value
                   && !value.Current.IsReadOnly;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }
}
