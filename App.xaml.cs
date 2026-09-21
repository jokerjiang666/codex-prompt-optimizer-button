using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer;

public partial class App : Application
{
    private MainController? _controller;
    private TrayIcon? _trayIcon;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 记录启动/退出标记：日志里只有 start 没有 exit，说明进程是被强制结束或崩溃的。
        WriteDiagnostic($"process=start pid={Environment.ProcessId}");
        _controller = new MainController(Dispatcher);
        _controller.Start();
        try
        {
            _trayIcon = new TrayIcon();
            _trayIcon.OpenSettingsRequested += (_, _) => _controller?.OpenSettings();
            _trayIcon.ExitRequested += (_, _) => RequestExit();
            _trayIcon.ShowStartupHint();
            WriteDiagnostic("tray=ready");
        }
        catch (Exception ex)
        {
            WriteDiagnostic($"tray=failed type={ex.GetType().Name}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteDiagnostic($"process=exit code={e.ApplicationExitCode}");

        try { _trayIcon?.Dispose(); } catch { }
        _controller?.Dispose();
        base.OnExit(e);

        // 托盘图标/后台任务都可能拖住进程；退出即确保进程结束。
        Environment.Exit(e.ApplicationExitCode);
    }

    /// <summary>统一退出入口（托盘菜单、右键菜单都走这里）。</summary>
    internal static void RequestExit()
    {
        WriteDiagnostic("exit=requested");
        Current?.Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        WriteDiagnostic($"process=session-ending reason={e.ReasonSessionEnding}");
        base.OnSessionEnding(e);
    }

    private static void OnProcessExit(object? sender, EventArgs e) =>
        WriteDiagnostic("process=process-exit");

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var suffix = e.Exception is COMException com ? $" hresult=0x{com.HResult:X8}" : string.Empty;
        WriteDiagnostic($"unhandled=dispatcher type={e.Exception.GetType().Name}{suffix}");
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteDiagnostic($"unhandled=task type={e.Exception.GetType().Name}");
        e.SetObserved();
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteDiagnostic($"unhandled=appdomain type={ex.GetType().Name} terminating={e.IsTerminating}");
    }

    internal static void WriteDiagnostic(string state)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "runtime-status.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {state}{Environment.NewLine}");
        }
        catch { }
    }
}
