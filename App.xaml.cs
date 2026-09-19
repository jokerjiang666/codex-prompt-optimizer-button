using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CodexInputEnhancer;

public partial class App : Application
{
    private MainController? _controller;

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteDiagnostic($"process=exit code={e.ApplicationExitCode}");
        _controller?.Dispose();
        base.OnExit(e);
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
