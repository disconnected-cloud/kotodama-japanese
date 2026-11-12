using Avalonia;
using Matsubakira.Services;

namespace Matsubakira;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Services.Perf.Mark("Main");
        Directory.CreateDirectory(ProgressStore.Folder);
        // Профиль JIT: второй и последующие запуски стартуют быстрее
        System.Runtime.ProfileOptimization.SetProfileRoot(ProgressStore.Folder);
        System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");

        // Служебный режим проверки VOICEVOX в отдельном процессе: без окон
        if (args.Length > 0 && args[0] == "--vv-check")
        {
            ProgressStore.Load();
            return Voicevox.RunSelfCheck();
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { LogCrash(e.ExceptionObject as Exception); ProgressStore.Save(); };
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
        ProgressStore.Load();
        Storage.RunPendingDeletes(); // до загрузки нативных библиотек

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => App.WithFonts(AppBuilder.Configure<App>())
        .UsePlatformDetect()
        .With(new X11PlatformOptions { WmClass = "Kotodama" })
        .LogToTrace();

    public static void LogCrash(Exception? ex)
    {
        try { File.AppendAllText(Path.Combine(ProgressStore.Folder, "crash.log"), $"[{DateTime.Now}] {ex}\n\n"); } catch { }
    }
}
