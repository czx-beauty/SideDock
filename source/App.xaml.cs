using System.IO;
using System.Windows;
using System.Threading;

namespace SideDock;
public partial class App : System.Windows.Application
{
    Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smoke = e.Args.Contains("--smoke-test") || e.Args.Contains("--startup-check");
        mutex = new Mutex(true, smoke ? "Local\\SideDock.Smoke" : "Local\\SideDock.Main", out bool first);
        if (!first) { System.Windows.MessageBox.Show("SideDock 已在运行。按 Ctrl+Alt+Space 或点击托盘图标唤出。", "SideDock"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => {
            Directory.CreateDirectory(Store.Root);
            File.AppendAllText(Path.Combine(Store.Root, "error.log"), $"{DateTime.Now:o} {args.Exception}\n");
            System.Windows.MessageBox.Show("发生错误，详情已写入本地 error.log。\n" + args.Exception.Message, "SideDock");
            args.Handled = true;
        };
        var window = new MainWindow(smoke);
        if (e.Args.Contains("--startup") || e.Args.Contains("--startup-check")) { window.ShowActivated = false; window.Opacity = 0; }
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
