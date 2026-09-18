using System.Windows;

namespace CodexInputEnhancer;

public partial class App : Application
{
    private MainController? _controller;

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
}
