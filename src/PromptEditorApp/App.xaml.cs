using System.IO;
using System.Windows;
using System.Windows.Threading;
using PromptEditor.Services;

namespace PromptEditor;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常处理：任何未处理异常都以弹窗 + 日志呈现，避免窗口静默消失
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        Settings = SettingsStore.Load();
        // 应用已保存的主题选择（默认 Light）
        ApplyTheme(Settings.Theme);

        try
        {
            MainWindow win;
            if (e.Args.Length > 0)
            {
                win = new MainWindow(e.Args);
            }
            else
            {
                win = new MainWindow();
            }
            MainWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            LogError("启动失败", ex);
            MessageBox.Show(
                "应用启动失败：\n\n" + ex +
                "\n\n日志已写入：" + LogPath,
                "AI 提示词 Markdown 编辑器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("UI 线程异常", e.Exception);
        MessageBox.Show(
            "发生未处理的异常：\n\n" + e.Exception +
            "\n\n日志已写入：" + LogPath,
            "AI 提示词 Markdown 编辑器", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // 尽量保持应用可用
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogError("AppDomain 异常", ex);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogError("后台任务异常", e.Exception);
        e.SetObserved();
    }

    public static string LogPath => Path.Combine(
        SettingsStore.AppDataDir, "error.log");

    private static void LogError(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.AppDataDir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch
        {
            // 日志写入失败时忽略
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Save(Settings);
        base.OnExit(e);
    }

    /// <summary>切换深色/浅色主题。</summary>
    public static void ApplyTheme(string theme)
    {
        var uri = new Uri($"Themes/{(theme == "Light" ? "Light" : "Dark")}.xaml", UriKind.Relative);
        Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
    }
}
