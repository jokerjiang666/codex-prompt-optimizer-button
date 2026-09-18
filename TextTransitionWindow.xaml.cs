using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CodexInputEnhancer;

public partial class TextTransitionWindow : Window
{
    public TextTransitionWindow()
    {
        InitializeComponent();
        ApplyTheme();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeWindowStyles.GetWindowLongPtr(handle, NativeWindowStyles.GwlExStyle).ToInt64();
        style |= NativeWindowStyles.WsExNoActivate | NativeWindowStyles.WsExToolWindow;
        _ = NativeWindowStyles.SetWindowLongPtr(handle, NativeWindowStyles.GwlExStyle, new IntPtr(style));
    }

    public async Task<bool> PlayReplaceAsync(
        string oldText,
        string newText,
        Rect composerBounds,
        Func<bool> writeText,
        bool reverse)
    {
        var textBounds = CalculateTextBounds(composerBounds);
        Left = textBounds.Left;
        Top = textBounds.Top;
        Width = textBounds.Width;
        Height = textBounds.Height;

        TransitionText.Text = oldText;
        TransitionText.Opacity = 1;
        TransitionText.Effect = new BlurEffect { Radius = 0 };
        TransitionText.RenderTransform = new TranslateTransform(0, 0);
        Show();

        var fadeOutMs = reverse ? 80 : 105;
        var fadeInMs = reverse ? 120 : 155;
        var outOffset = reverse ? 3 : -2;
        var inOffset = reverse ? -3 : 4;

        Animate(TransitionText, 1, 0, 0, outOffset, fadeOutMs, reverse ? 2 : 4);
        await Task.Delay(fadeOutMs);

        if (!writeText())
        {
            Hide();
            return false;
        }

        TransitionText.Text = newText;
        TransitionText.Opacity = 0;
        TransitionText.Effect = new BlurEffect { Radius = 0 };
        TransitionText.RenderTransform = new TranslateTransform(0, inOffset);
        Animate(TransitionText, 0, 1, inOffset, 0, fadeInMs, 0);
        await Task.Delay(fadeInMs);

        Hide();
        return true;
    }

    private static Rect CalculateTextBounds(Rect composer)
    {
        var width = Math.Max(120, composer.Width - 92);
        var height = Math.Max(32, composer.Height - 44);
        return new Rect(composer.Left + 8, composer.Top + 6, width, height);
    }

    private static void Animate(
        UIElement element,
        double opacityFrom,
        double opacityTo,
        double yFrom,
        double yTo,
        int durationMs,
        double blurTo)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityFrom, opacityTo, duration));

        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(yFrom, yTo, duration));

        if (element.Effect is BlurEffect blur)
            blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(blur.Radius, blurTo, duration));
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

        Surface.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#1F2020" : "#FBFCFA"));
        TransitionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#ECEEEC" : "#272A28"));
    }
}
