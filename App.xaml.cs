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
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _controller = new MainController(Dispatcher);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }

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

    private static void WriteDiagnostic(string state)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "runtime-status.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {state}{Environment.NewLine}");
        }
        catch { }
    }
}
