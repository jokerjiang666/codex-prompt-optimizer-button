using Microsoft.Win32;
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
    private bool _isOptimizing;
    private IntPtr _targetWindowHandle;
    private readonly DispatcherTimer _hintTimer;
    private ToolTip? _hintToolTip;

    public event EventHandler? OptimizeRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RecentRequested;
    public event EventHandler? ClearRecentRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplyTheme();
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
        Width = visible ? 60 : 30;

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

    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void RecentMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        RecentRequested?.Invoke(this, EventArgs.Empty);

    private void ClearRecentMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ClearRecentRequested?.Invoke(this, EventArgs.Empty);

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
        Application.Current.Shutdown();
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
