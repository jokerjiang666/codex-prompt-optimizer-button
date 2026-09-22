using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class WindowTracker
{
    // UIA 遍历整棵元素树的代价很高：同一轮刷新内复用一次 Button 快照，
    // 顶层窗口枚举做 1 秒缓存，避免 350ms 轮询反复创建大量跨进程引用。
    private static readonly TimeSpan ButtonSnapshotLifetime = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan HostSnapshotLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RootSnapshotLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ComposerCacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SessionCacheLifetime = TimeSpan.FromSeconds(2);

    private IntPtr _buttonSnapshotHandle;
    private List<ButtonInfo>? _buttonSnapshot;
    private DateTimeOffset _buttonSnapshotAt;

    private string? _sessionCacheKey;
    private DateTimeOffset _sessionCacheAt;

    private IntPtr _composerCacheHandle;
    private AutomationElement? _composerCacheElement;
    private DateTimeOffset _composerCacheAt;

    private IntPtr _rootHandle;
    private AutomationElement? _rootSnapshot;
    private DateTimeOffset _rootSnapshotAt;

    /// <summary>
    /// 按钮快照只保留纯数据（名称/类名/矩形/状态），不再持有 AutomationElement。
    /// 之前缓存 250+ 个 UIA 元素，每个元素都是一个跨进程 RCW，2.5 次/秒地整批替换导致内存持续上涨。
    /// </summary>
    private sealed record ButtonInfo(string Name, string ClassName, Rect Bounds, bool IsEnabled, bool IsOffscreen);

    private List<(IntPtr Handle, Rect Bounds)>? _hostSnapshot;
    private DateTimeOffset _hostSnapshotAt;

    public string? TryGetSessionIdentity(IntPtr windowHandle)
    {
        // 会话标识每轮都要用，但只在切会话时变化：缓存 2 秒，省掉大量 UIA 属性读取。
        if (_sessionCacheKey is not null && DateTimeOffset.UtcNow - _sessionCacheAt < SessionCacheLifetime)
            return _sessionCacheKey;

        try
        {
            var root = GetRoot(windowHandle);
            if (root is null) return null;

            var headerTitle = TryGetHeaderThreadTitle(root, GetButtonSnapshot(windowHandle, root));
            if (!string.IsNullOrWhiteSpace(headerTitle))
                return CacheSession($"{root.Current.ProcessId}:{headerTitle}");

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
            return CacheSession($"{best.Current.ProcessId}:{bestName}");
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }

    /// <summary>
    /// 读取当前窗口 header 的会话标题（供上下文定位使用）。复用已有的按钮快照缓存，失败返回 null。
    /// </summary>
    public string? TryGetThreadTitle(IntPtr windowHandle)
    {
        try
        {
            var root = GetRoot(windowHandle);
            if (root is null) return null;
            return TryGetHeaderThreadTitle(root, GetButtonSnapshot(windowHandle, root));
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }
    private static string? TryGetHeaderThreadTitle(AutomationElement root, IReadOnlyList<ButtonInfo> buttons)
    {
        try
        {
            var rootBounds = root.Current.BoundingRectangle;
            if (rootBounds.IsEmpty) return null;

            ButtonInfo? best = null;
            var bestTop = double.MaxValue;

            foreach (var button in buttons)
            {
                if (button.IsOffscreen || !button.IsEnabled) continue;
                if (string.IsNullOrWhiteSpace(button.Name)) continue;
                if (!button.ClassName.Contains("max-w-[320px]", StringComparison.OrdinalIgnoreCase)
                    || !button.ClassName.Contains("truncate", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bounds = button.Bounds;
                if (bounds.IsEmpty) continue;
                if (bounds.Top < rootBounds.Top || bounds.Top > rootBounds.Top + 130) continue;
                if (bounds.Left < rootBounds.Left + 380 || bounds.Left > rootBounds.Left + 1100) continue;

                if (bounds.Top >= bestTop) continue;
                best = button;
                bestTop = bounds.Top;
            }

            return best?.Name;
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
                var root = GetRoot(host.Handle);
                if (root is null) continue;

                var buttons = GetButtonSnapshot(host.Handle, root);
                var processId = root.Current.ProcessId;
                // 输入框元素在窗口内基本稳定：缓存 2 秒，避免每轮都做整棵树的 Edit/Document 扫描。
                var element = TryGetCachedComposer(host.Handle);
                if (element is null)
                {
                    element = FindBestCandidate(root, host.Bounds, ControlType.Edit)
                              ?? FindBestCandidate(root, host.Bounds, ControlType.Document)
                              ?? FindBestGlobalCandidate(host.Bounds, ControlType.Edit, processId)
                              ?? FindBestGlobalCandidate(host.Bounds, ControlType.Document, processId);

                    if (element is not null) CacheComposer(host.Handle, element);
                }

                if (element is not null)
                {
                    var composerBounds = FindComposerBounds(element, host.Bounds);
                    var permissionAnchor = FindPermissionAnchor(host.Bounds, buttons)
                                           ?? FindGlobalPermissionAnchor(host.Bounds, root.Current.ProcessId);
                    return new ComposerTarget(host.Handle, element, composerBounds, permissionAnchor);
                }
            }

            return null;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }

    public bool IsGenerationInProgress(IntPtr windowHandle)
    {
        try
        {
            var root = GetRoot(windowHandle);
            if (root is null) return false;

            foreach (var button in GetButtonSnapshot(windowHandle, root))
            {
                if (button.IsOffscreen) continue;

                var name = button.Name;
                if (name.Contains("停止回答", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("停止生成", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("stop generating", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "停止", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "stop", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }

        return false;
    }

    private string CacheSession(string identity)
    {
        _sessionCacheKey = identity;
        _sessionCacheAt = DateTimeOffset.UtcNow;
        return identity;
    }
    private AutomationElement? TryGetCachedComposer(IntPtr windowHandle)
    {
        if (_composerCacheElement is null || _composerCacheHandle != windowHandle) return null;
        if (DateTimeOffset.UtcNow - _composerCacheAt > ComposerCacheLifetime) return null;

        try
        {
            var bounds = _composerCacheElement.Current.BoundingRectangle;
            return bounds.IsEmpty ? null : _composerCacheElement;
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }
        return null;
    }

    private void CacheComposer(IntPtr windowHandle, AutomationElement element)
    {
        _composerCacheHandle = windowHandle;
        _composerCacheElement = element;
        _composerCacheAt = DateTimeOffset.UtcNow;
    }

    /// <summary>会话或窗口切换时让缓存的输入框元素失效。</summary>
    public void InvalidateComposerCache()
    {
        _composerCacheElement = null;
        _composerCacheHandle = IntPtr.Zero;
    }
    private AutomationElement? GetRoot(IntPtr windowHandle)
    {
        if (_rootSnapshot is not null
            && _rootHandle == windowHandle
            && DateTimeOffset.UtcNow - _rootSnapshotAt < RootSnapshotLifetime)
        {
            return _rootSnapshot;
        }

        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            if (root is null) return null;

            _rootHandle = windowHandle;
            _rootSnapshot = root;
            _rootSnapshotAt = DateTimeOffset.UtcNow;
            return root;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }
    private IReadOnlyList<(IntPtr Handle, Rect Bounds)> FindHostWindows()
    {
        if (_hostSnapshot is not null
            && DateTimeOffset.UtcNow - _hostSnapshotAt < HostSnapshotLifetime)
        {
            return _hostSnapshot;
        }

        _hostSnapshot = CollectHostWindows();
        _hostSnapshotAt = DateTimeOffset.UtcNow;
        return _hostSnapshot;
    }

    private static List<(IntPtr Handle, Rect Bounds)> CollectHostWindows()
    {
        var processIds = new HashSet<int>();

        foreach (var processName in new[] { "Codex", "ChatGPT" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                // Process 持有原生进程句柄，必须释放。
                using (process)
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
        }

        var hosts = new List<(IntPtr Handle, Rect Bounds)>();
        if (processIds.Count == 0) return hosts;

        // 用 Win32 枚举顶层窗口：比 desktop.FindAll(Children) 便宜得多，也不再产生上百个 UIA 元素。
        EnumWindows((handle, _) =>
        {
            try
            {
                GetWindowThreadProcessId(handle, out var processId);
                if (!processIds.Contains((int)processId) || !IsWindowVisible(handle)) return true;
                if (!GetWindowRect(handle, out var rect)) return true;

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < 500 || height < 400) return true;

                hosts.Add((handle, new Rect(rect.Left, rect.Top, width, height)));
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        return hosts.OrderByDescending(host => host.Bounds.Width * host.Bounds.Height).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    // 头部标题、权限锚点、生成状态都要遍历 Button 集合，
    // 用一个很短的快照复用，避免同一轮刷新里重复遍历整棵元素树。
    private List<ButtonInfo> GetButtonSnapshot(IntPtr windowHandle, AutomationElement root)
    {
        if (_buttonSnapshot is not null
            && _buttonSnapshotHandle == windowHandle
            && DateTimeOffset.UtcNow - _buttonSnapshotAt < ButtonSnapshotLifetime)
        {
            return _buttonSnapshot;
        }

        var buttons = new List<ButtonInfo>();
        try
        {
            var found = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            foreach (AutomationElement button in found)
            {
                try
                {
                    buttons.Add(new ButtonInfo(
                        (button.Current.Name ?? string.Empty).Trim(),
                        button.Current.ClassName ?? string.Empty,
                        button.Current.BoundingRectangle,
                        button.Current.IsEnabled,
                        button.Current.IsOffscreen));
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }

        _buttonSnapshotHandle = windowHandle;
        _buttonSnapshot = buttons;
        _buttonSnapshotAt = DateTimeOffset.UtcNow;
        return buttons;
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

    private static Rect? FindPermissionAnchor(Rect hostBounds, IReadOnlyList<ButtonInfo> buttons)
    {
        Rect? best = null;

        foreach (var button in buttons)
        {
            if (!button.IsEnabled || button.IsOffscreen) continue;
            if (!button.Name.Contains("权限", StringComparison.OrdinalIgnoreCase)
                && !button.Name.Contains("访问", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bounds = button.Bounds;
            if (bounds.IsEmpty) continue;
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            if (!hostBounds.Contains(center)) continue;
            if (bounds.Top < hostBounds.Top + hostBounds.Height * 0.55) continue;

            if (best is null || bounds.Left < best.Value.Left)
                best = bounds;
        }

        if (best is null) return null;

        return AnchorPlacement.ExtendAcrossLeftCluster(best.Value, buttons.Select(b => b.Bounds));
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
            var candidates = new List<Rect>();

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

                    var bounds = button.Current.BoundingRectangle;
                    if (!bounds.IsEmpty) candidates.Add(bounds);

                    var name = button.Current.Name ?? string.Empty;
                    if (!name.Contains("权限", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("访问", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (bounds.IsEmpty) continue;
                    var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    if (!hostBounds.Contains(center)) continue;
                    if (bounds.Top < hostBounds.Top + hostBounds.Height * 0.55) continue;

                    if (best is null || bounds.Left < best.Value.Left)
                        best = bounds;
                }
                catch (ElementNotAvailableException) { }
            }

            if (best is null) return null;

            return AnchorPlacement.ExtendAcrossLeftCluster(best.Value, candidates);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }


    /// <summary>
    /// 输入框右下角的发送键。名称会随状态变化（发送 / 加入队列 / Send…），
    /// 因此用「名称白名单 + 与输入框同行 + 右半边 + 排除停止类按钮」定位，取最右侧那个。
    /// </summary>
    public AutomationElement? TryFindSendButton(IntPtr windowHandle, Rect composerBounds, out string buttonName)
    {
        buttonName = string.Empty;
        try
        {
            var root = GetRoot(windowHandle);
            if (root is null) return null;

            // 只有真的要发送时才做一次 FindAll：既能拿到可 Invoke 的元素，
            // 又不会把 250+ 个 UIA 元素长期留在内存里（发送是低频操作）。
            var found = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            AutomationElement? best = null;
            var bestLeft = double.MinValue;

            foreach (AutomationElement button in found)
            {
                try
                {
                    if (button.Current.IsOffscreen || !button.Current.IsEnabled) continue;

                    var name = (button.Current.Name ?? string.Empty).Trim();
                    if (!IsSendButtonName(name)) continue;

                    var bounds = button.Current.BoundingRectangle;
                    if (bounds.IsEmpty) continue;
                    if (bounds.Top > composerBounds.Bottom + 12) continue;
                    if (bounds.Bottom < composerBounds.Top) continue;
                    if (bounds.Left < composerBounds.Left + composerBounds.Width * 0.5) continue;
                    if (bounds.Left <= bestLeft) continue;

                    best = button;
                    bestLeft = bounds.Left;
                    buttonName = name;
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }

            return best;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (COMException) { return null; }
    }

    private static bool IsSendButtonName(string name)
    {
        if (name.Length == 0) return false;
        if (IsStopButtonName(name)) return false;

        return name is "发送" or "加入队列" or "排队" or "提交" or "发送消息"
               || name.Contains("发送", StringComparison.Ordinal)
               || name.Contains("Send", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Queue", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Submit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStopButtonName(string name) =>
        name.Contains("停止", StringComparison.Ordinal)
        || name.Contains("中断", StringComparison.Ordinal)
        || name.Contains("Stop", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 生成结束后扫描会话尾部，判断是否为中断/报错。
    /// 只在「生成中 → 空闲」翻转时调用，避免高频整树遍历。
    /// </summary>
    public bool TryGetInterruptionSignal(IntPtr windowHandle, Rect composerBounds, out string signature, out string source)
    {
        signature = string.Empty;
        source = string.Empty;

        try
        {
            var root = GetRoot(windowHandle);
            if (root is null) return false;

            var tailTop = composerBounds.Top - 620;

            var textCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            var texts = root.FindAll(TreeScope.Descendants, textCondition);

            var scanned = 0;
            for (var i = texts.Count - 1; i >= 0 && scanned < 120; i--)
            {
                try
                {
                    var element = texts[i];
                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Height <= 0) continue;
                    if (bounds.Bottom > composerBounds.Top + 4 || bounds.Bottom < tailTop) continue;

                    scanned++;
                    if (!InterruptionDetector.IsInterruption(element.Current.Name, out var found)) continue;

                    signature = found;
                    source = "text";
                    return true;
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }

            // 备用信号：错误旁边出现「重试」按钮。
            foreach (var button in GetButtonSnapshot(windowHandle, root))
            {
                if (button.IsOffscreen || !button.IsEnabled) continue;
                if (!IsRetryButtonName(button.Name)) continue;

                var bounds = button.Bounds;
                if (bounds.IsEmpty) continue;
                if (bounds.Bottom > composerBounds.Top + 4 || bounds.Bottom < tailTop) continue;

                signature = "retry-button";
                source = "retry-button";
                return true;
            }

            return false;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (COMException) { return false; }
    }

    private static bool IsRetryButtonName(string name) =>
        name is "重试" or "Retry" or "重新尝试" or "再试一次";
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
