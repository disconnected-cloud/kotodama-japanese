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
        // Служебные режимы проверки интерфейса (CI) работают только на чтение и запускаются друг за другом — им мьютекс не нужен
        bool tool = args.Any(a => a is "--snap" or "--cliptest" or "--perf" or "--rerender" or "--rutts");
        if (!tool)
        {
            // перезапуск из настроек: прежняя копия ещё закрывается — ждём её, а не показываем её окно
            if (!SingleInstance.Acquire(ProgressStore.Folder, args.Contains(RestartArg) ? 20000 : 0))
            {
                if (SingleInstance.SignalFirst(ProgressStore.Folder)) return 0;
                // первая копия не отвечает (закрывается) — дождаться её выхода
                if (!SingleInstance.Acquire(ProgressStore.Folder, 15000)) return 0;
            }
            SingleInstance.Listen(ProgressStore.Folder, () => Avalonia.Threading.Dispatcher.UIThread.Post(ShowMainWindow));
        }
        // Прогресс из обработчика падения не сохраняем: сбой мог случиться посреди изменения, и на диск ушло бы
        // полуизменённое состояние. Последнее отложенное сохранение (раз в 2 с) уже на диске.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashReport.Warn("Необработанная ошибка задачи", e.Exception); e.SetObserved(); };
        ProgressStore.Load();
        Storage.RunPendingDeletes(); // до загрузки нативных библиотек

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Аргумент перезапуска из настроек: новая копия ждёт, пока закроется прежняя.</summary>
    public const string RestartArg = "--restarted";

    /// <summary>Вторая копия попросила показать окно: разворачиваем и выводим на передний план (в т.ч. из трея).</summary>
    static void ShowMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w }) return;
        w.Show();
        if (w.WindowState == Avalonia.Controls.WindowState.Minimized) w.WindowState = Avalonia.Controls.WindowState.Normal;
        w.Activate();
        w.Topmost = true; w.Topmost = false; // Windows не даёт фоновому процессу забрать фокус — так окно хотя бы всплывает
    }

    public static AppBuilder BuildAvaloniaApp() => App.WithFonts(AppBuilder.Configure<App>())
        .UsePlatformDetect()
        .With(new X11PlatformOptions { WmClass = "Kotodama" })
        .LogToTrace();

    public static void LogCrash(Exception? ex)
    {
        try { File.AppendAllText(Path.Combine(ProgressStore.Folder, "crash.log"), $"[{DateTime.Now}] {ex}\n\n"); } catch { } // журнал недоступен (диск, права) — сообщить об этом некуда
    }
}
