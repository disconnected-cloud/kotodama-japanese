using System.Windows;
using Matsubakira.Services;

namespace Matsubakira;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Профиль JIT: второй и последующие запуски стартуют быстрее
        System.IO.Directory.CreateDirectory(ProgressStore.Folder);
        System.Runtime.ProfileOptimization.SetProfileRoot(ProgressStore.Folder);
        System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");

        // Служебный режим проверки VOICEVOX в отдельном процессе: без окон и без обработчика с MessageBox
        if (e.Args.Length > 0 && e.Args[0] == "--vv-check")
        {
            ProgressStore.Load();
            Environment.Exit(Voicevox.RunSelfCheck());
        }
        DispatcherUnhandledException += (_, ex) =>
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(ProgressStore.Folder, "crash.log"), $"[{DateTime.Now}] {ex.Exception}\n\n"); } catch { }
            ProgressStore.Save();
            ex.Handled = true;
            // Некритичные ошибки — коротким уведомлением, без модального окна
            if (Shell.Main != null && Shell.Main.IsLoaded) Shell.Toast("Что-то пошло не так — записал в журнал, продолжаем", "!");
            else MessageBox.Show("Произошла ошибка, но прогресс сохранён.\n\n" + ex.Exception.Message, "Kotodama", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) => ex.SetObserved();
        ProgressStore.Load();
        // Режим проверки VOICEVOX в отдельном процессе (см. Voicevox.SelfCheckAsync)
        // Тема выбирается до создания окна: палитра → стили → шаблоны
        Storage.RunPendingDeletes(); // до загрузки нативных библиотек
        bool light = ProgressStore.P.LightTheme || Environment.GetCommandLineArgs().Contains("--light");
        var colors = new ResourceDictionary { Source = new Uri(light ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml", UriKind.Relative) };
        ApplyAccent(colors, ProgressStore.P.AccentColor, light);
        Resources.MergedDictionaries.Add(colors);
        foreach (var dict in new[] { "Themes/Theme.xaml", "Themes/Templates.xaml" })
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) });
        // Тяжёлое — в фоне, чтобы окно появилось сразу
        Task.Run(Db.Preload);
        Task.Run(Speech.Init);
        // VOICEVOX скачан, но не проверен — проверяем в фоне (в отдельном процессе)
        if (e.Args.Length == 0 && Voicevox.Downloaded && !ProgressStore.P.Flags.Contains("vv-ok"))
            _ = Task.Run(async () =>
            {
                var (ok, _) = await Voicevox.SelfCheckAsync();
                if (ok) Current.Dispatcher.Invoke(() => Shell.Toast("Офлайн-голоса VOICEVOX готовы", "声"));
            });
        base.OnStartup(e);
    }

    /// <summary>Акцентный цвет: подменяем кисти палитры до загрузки стилей (стили берут их через StaticResource).</summary>
    static void ApplyAccent(ResourceDictionary d, string accent, bool light)
    {
        var (dark, lite) = accent switch
        {
            "mint" => ("#4FD1B5", "#1E9E85"),
            "indigo" => ("#8C9EFF", "#4F5FD9"),
            "amber" => ("#F2B45A", "#C07F15"),
            _ => ("", ""),
        };
        if (dark.Length == 0) return;
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(light ? lite : dark);
        System.Windows.Media.Brush Frozen(System.Windows.Media.Brush b) { b.Freeze(); return b; }
        d["Accent"] = Frozen(new System.Windows.Media.SolidColorBrush(c));
        d["AccentSoft"] = Frozen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, c.R, c.G, c.B)));
        d["AccentGradient"] = Frozen(new System.Windows.Media.SolidColorBrush(c));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ProgressStore.Save();
        base.OnExit(e);
    }
}
