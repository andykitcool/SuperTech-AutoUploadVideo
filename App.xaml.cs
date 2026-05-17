using System.IO;
using System.Windows;

namespace Supertech.AutoUploadVideo;

public partial class App : System.Windows.Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Supertech", "AutoUploadVideo");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers to catch and log crashes
        DispatcherUnhandledException += (s, args) =>
        {
            WriteCrashLog("UI线程异常", args.Exception);
            System.Windows.MessageBox.Show($"发生未处理的异常：\n{args.Exception.Message}\n\n详情已写入日志。", "意外错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog("后台线程异常", ex);
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            WriteCrashLog("Task异常", args.Exception);
            args.SetObserved();
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var loginWindow = new LoginWindow();
        if (loginWindow.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"主界面启动失败：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    public static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var logPath = Path.Combine(LogDirectory, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {exception}\n{new string('-', 60)}\n";
            File.AppendAllText(logPath, entry);
        }
        catch { /* best effort */ }
    }
}
