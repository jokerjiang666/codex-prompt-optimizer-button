using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CodexInputEnhancer.Services;

/// <summary>
/// 任务栏通知区图标：一眼能看出程序在运行，并提供「打开设置 / 退出」。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _ownedIcon;
    private bool _disposed;

    internal event EventHandler? OpenSettingsRequested;
    internal event EventHandler? ExitRequested;

    internal TrayIcon()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("打开设置");
        open.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exit);

        _ownedIcon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _ownedIcon ?? SystemIcons.Application,
            Text = "Codex 提示词优化按钮（运行中）",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    internal void ShowStartupHint()
    {
        try
        {
            _icon.BalloonTipTitle = "Codex 提示词优化按钮";
            _icon.BalloonTipText = "已在后台运行。右键托盘图标可打开设置或退出。";
            _icon.BalloonTipIcon = ToolTipIcon.Info;
            _icon.ShowBalloonTip(3000);
        }
        catch { }
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var fromExe = Icon.ExtractAssociatedIcon(exe);
                if (fromExe is not null) return fromExe;
            }

            var file = Path.Combine(AppContext.BaseDirectory, "Assets", "CodexInputEnhancer.ico");
            return File.Exists(file) ? new Icon(file) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }

        _ownedIcon?.Dispose();
    }
}