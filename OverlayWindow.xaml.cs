using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexInputEnhancer;

public partial class OverlayWindow : Window
{
    private bool _hasUndo;
    private bool _hasContinue = true;
    private bool _isOptimizing;
    private IntPtr _targetWindowHandle;
    private readonly DispatcherTimer _hintTimer;
    private ToolTip? _hintToolTip;
    private IntPtr _menuMouseHook;
    private LowLevelMouseProc? _menuMouseHookProc;
    private ContextMenu? _openContextMenu;

    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcRButtonDown = 0x00A4;
    private const int WmNcMButtonDown = 0x00A7;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point32 Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    public event EventHandler<string>? OptimizeWithTemplateRequested;
    public event EventHandler? OptimizeRequested;
    public event EventHandler? ContinueRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RecentRequested;
    public event EventHandler? ClearRecentRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplyTheme();
        UpdateWidth();
        _hintTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1800), DispatcherPriority.Background,
            (_, _) => CloseHint(), Dispatcher);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeWindowStyles.GetWindowLongPtr(handle, NativeWindowStyles.GwlExStyle).ToInt64();
        style |= NativeWindowStyles.WsExNoActivate | NativeWindowStyles.WsExToolWindow;
        _ = NativeWindowStyles.SetWindowLongPtr(handle, NativeWindowStyles.GwlExStyle, new IntPtr(style));
    }

    public void SetUndoVisible(bool visible, bool animate = true)
    {
        if (_hasUndo == visible) return;
        _hasUndo = visible;
        UpdateWidth();

        if (!visible)
        {
            UndoButton.Visibility = Visibility.Collapsed;
            return;
        }

        UndoButton.Visibility = Visibility.Visible;
        if (!animate)
        {
            UndoButton.Opacity = 1;
            return;
        }

        UndoButton.Opacity = 0;
        UndoButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    /// <summary>「继续」按钮显隐（设置里可关闭，关闭后悬浮窗只剩优化按钮）。</summary>
    public void SetContinueVisible(bool visible, bool animate = true)
    {
        if (_hasContinue == visible) return;
        _hasContinue = visible;
        UpdateWidth();

        if (!visible)
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            return;
        }

        ContinueButton.Visibility = Visibility.Visible;
        if (!animate)
        {
            ContinueButton.Opacity = 1;
            return;
        }

        ContinueButton.Opacity = 0;
        ContinueButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    // 窗口宽度 = 可见按钮数 × 30；右边缘始终锚定在目标芯片右侧，因此向左扩展。
    private void UpdateWidth() =>
        Width = 30 + (_hasContinue ? 30 : 0) + (_hasUndo ? 30 : 0);

    public void SetOptimizing(bool optimizing)
    {
        _isOptimizing = optimizing;
        OptimizeButton.Opacity = 1;
        OptimizeButton.Tag = optimizing ? "Loading" : null;

        if (optimizing)
        {
            DefaultOptimizeIcon.Visibility = Visibility.Collapsed;
            LoadingVectorIcon.Visibility = Visibility.Visible;
            LoadingSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingSpinnerRotate.Angle = 0;
            // 交给 WPF 动画时钟驱动，避免 UI 线程回调抖动在循环边界累积成卡顿。
            LoadingSpinnerRotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }
        else
        {
            LoadingSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingSpinnerRotate.Angle = 0;
            LoadingVectorIcon.Visibility = Visibility.Collapsed;
            DefaultOptimizeIcon.Visibility = Visibility.Visible;
        }
    }

    public void SetRecentCount(int count) => RecentMenuItem.Header = $"最近发送 ({count})";

    public void ShowHint(string message)
    {
        _hintToolTip ??= new ToolTip
        {
            PlacementTarget = OptimizeButton,
            Placement = PlacementMode.Top,
            VerticalOffset = -6,
            StaysOpen = true,
            Padding = new Thickness(10, 6, 10, 6),
            Background = Brush("#FEFFFE"),
            Foreground = Brush("#202321"),
            BorderBrush = Brush("#D9DDD8"),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13
        };

        _hintToolTip.Content = message;
        _hintToolTip.IsOpen = false;
        _hintToolTip.IsOpen = true;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void CloseHint()
    {
        _hintTimer.Stop();
        if (_hintToolTip is not null)
            _hintToolTip.IsOpen = false;
    }

    public void PositionNearComposer(Rect composerBounds, Rect? permissionAnchorBounds = null)
    {
        var dpi = _targetWindowHandle != IntPtr.Zero
            ? NativeWindowStyles.GetDpiForWindow(_targetWindowHandle)
            : 96u;
        if (dpi == 0) dpi = 96;

        var scale = dpi / 96.0;
        var widthPx = Math.Max(1, (int)Math.Round(Width * scale));
        var heightPx = Math.Max(1, (int)Math.Round(Height * scale));
        int leftPx;
        int topPx;
        if (permissionAnchorBounds is { } anchor && !anchor.IsEmpty)
        {
            var gapPx = (int)Math.Round(6 * scale);
            leftPx = (int)Math.Round(anchor.Right + gapPx);
            topPx = (int)Math.Round(anchor.Top + (anchor.Height - heightPx) / 2);
        }
        else
        {
            leftPx = (int)Math.Round(composerBounds.Left + 132 * scale);
            topPx = (int)Math.Round(composerBounds.Bottom - heightPx - 7 * scale);
        }

        var handle = new WindowInteropHelper(this).EnsureHandle();
        _ = NativeWindowStyles.SetWindowPos(
            handle,
            IntPtr.Zero,
            leftPx,
            topPx,
            widthPx,
            heightPx,
            NativeWindowStyles.SwpNoZOrder | NativeWindowStyles.SwpNoActivate);
    }

    public void SetTargetWindow(IntPtr targetWindowHandle)
    {
        _targetWindowHandle = targetWindowHandle;
    }

    private void OptimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isOptimizing) CancelRequested?.Invoke(this, EventArgs.Empty);
        else OptimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 重建右键菜单「优化方式」子菜单：悬停即展开，点某个模板立刻用它优化当前草稿。
    /// </summary>
    public void SetOptimizeModes(IReadOnlyList<(string Id, string Name)> templates, string? activeId)
    {
        OptimizeModeMenuItem.Items.Clear();

        if (templates.Count == 0)
        {
            OptimizeModeMenuItem.Items.Add(new MenuItem { Header = "（暂无模板）", IsEnabled = false });
            return;
        }

        foreach (var (id, name) in templates)
        {
            var item = new MenuItem
            {
                Header = name,
                IsCheckable = true,
                IsChecked = string.Equals(id, activeId, StringComparison.Ordinal),
                Tag = id
            };
            item.Click += (sender, _) =>
            {
                if (sender is MenuItem { Tag: string templateId })
                    OptimizeWithTemplateRequested?.Invoke(this, templateId);
            };
            OptimizeModeMenuItem.Items.Add(item);
        }

        OptimizeModeMenuItem.Items.Add(new Separator());

        var manage = new MenuItem { Header = "管理模板…" };
        manage.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        OptimizeModeMenuItem.Items.Add(manage);
    }
    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void RecentMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        RecentRequested?.Invoke(this, EventArgs.Empty);

    private void ClearRecentMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ClearRecentRequested?.Invoke(this, EventArgs.Empty);

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isOptimizing) return;
        ContinueRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isOptimizing) return;
        UndoButton.RenderTransformOrigin = new Point(0.5, 0.5);
        UndoButton.RenderTransform = new RotateTransform();
        UndoButton.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(70)) { AutoReverse = true });
        UndoRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        App.WriteDiagnostic("exit=menu-command");
        App.RequestExit();
    }

    private void ContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        _openContextMenu = menu;
        _menuMouseHookProc = MenuMouseHookCallback;
        _menuMouseHook = SetWindowsHookEx(WhMouseLl, _menuMouseHookProc, GetModuleHandle(null), 0);
    }

    private void ContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _openContextMenu = null;
        if (_menuMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_menuMouseHook);
            _menuMouseHook = IntPtr.Zero;
        }

        _menuMouseHookProc = null;
    }

    private IntPtr MenuMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _openContextMenu is { IsOpen: true })
        {
            var message = wParam.ToInt32();
            if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown
                or WmNcLButtonDown or WmNcRButtonDown or WmNcMButtonDown)
            {
                var hookData = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                if (!IsPointInsideContextMenu(hookData.Point))
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_openContextMenu is { IsOpen: true })
                            _openContextMenu.IsOpen = false;
                    });
                }
            }
        }

        return CallNextHookEx(_menuMouseHook, nCode, wParam, lParam);
    }

    private bool IsPointInsideContextMenu(Point32 point)
    {
        if (_openContextMenu is null) return true;

        try
        {
            if (IsPointInsideHandle(point, PresentationSource.FromVisual(_openContextMenu) as HwndSource)) return true;

            // 子菜单是独立弹窗，点击时也要算「菜单内部」，否则会被低层鼠标钩子误判成外部点击。
            foreach (var item in _openContextMenu.Items.OfType<MenuItem>())
            {
                if (!item.IsSubmenuOpen) continue;
                if (IsPointInsideHandle(point, PresentationSource.FromVisual(item) as HwndSource)) return true;

                foreach (var nested in item.Items.OfType<MenuItem>())
                {
                    if (!nested.IsSubmenuOpen) continue;
                    if (IsPointInsideHandle(point, PresentationSource.FromVisual(nested) as HwndSource)) return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPointInsideHandle(Point32 point, HwndSource? source)
    {
        if (source is null || source.Handle == IntPtr.Zero) return false;

        var handle = WindowFromPoint(point);
        if (handle == IntPtr.Zero) return false;

        return GetAncestor(handle, GaRoot) == source.Handle;
    }

    private void ApplyTheme()
    {
        var isDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            isDark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { }

        Resources["IconBrush"] = Brush(isDark ? "#F3F5F3" : "#181A19");
        Resources["UndoIconBrush"] = Brush(isDark ? "#C9CECA" : "#4D524E");
        Resources["HoverBrush"] = Brush(isDark ? "#2B2E2C" : "#E8EBE7");
        Resources["PressedBrush"] = Brush(isDark ? "#363A37" : "#DDE1DC");
        Resources["MenuSurfaceBrush"] = Brush(isDark ? "#252825" : "#FEFFFE");
        Resources["MenuTextBrush"] = Brush(isDark ? "#F2F4F2" : "#202321");
        Resources["MenuSubtleBrush"] = Brush(isDark ? "#3A3E3B" : "#E4E7E3");
        Resources["MenuHoverBrush"] = Brush(isDark ? "#303431" : "#F1F3F0");
        Resources["MenuPressedBrush"] = Brush(isDark ? "#3A3E3B" : "#E7EAE6");
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));
}
