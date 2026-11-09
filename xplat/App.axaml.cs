using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Matsubakira.Services;

namespace Matsubakira;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (!ProgressStore.IsLoaded) ProgressStore.Load(); // на телефоне нет Program.Main
        bool light = ProgressStore.P.LightTheme || Environment.GetCommandLineArgs().Contains("--light");
        RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        ApplyPalette(Resources, light, ProgressStore.P.AccentColor);

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashReport.Log(e.Exception);
            ProgressStore.Save();
            e.Handled = true;
            // Некритичные ошибки — коротким уведомлением, без модального окна
            try { if (Shell.Main != null) Shell.Toast("Что-то пошло не так — записал в журнал, продолжаем", "!"); } catch { }
        };

        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView(); // телефон: интерфейс без окна
#if !MOBILE
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) => ProgressStore.Save();
            desktop.Exit += (_, _) => ProgressStore.Save();
        }
#endif
        // Тяжёлое — в фоне, чтобы окно появилось сразу
        Task.Run(Db.Preload);
        _ = JDict.LoadAsync(); // словарь нужен латинице, распознаванию речи и чтению — грузим в фоне сразу
        if (Environment.GetCommandLineArgs().Length <= 1 && Voicevox.Downloaded && !ProgressStore.P.Flags.Contains("vv-ok"))
            _ = Task.Run(async () =>
            {
                var (ok, _) = await Voicevox.SelfCheckAsync();
                if (ok) Dispatcher.UIThread.Post(() => Shell.Toast("Офлайн-голоса VOICEVOX готовы", "声"));
            });
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Палитра «тихий минимализм, один акцент (сакура)». Кисти кладутся в ресурсы до создания окна.</summary>
    public static void ApplyPalette(IResourceDictionary r, bool light, string accent)
    {
        var dark = new Dictionary<string, string>
        {
            ["Bg"] = "#0F1014", ["Sidebar"] = "#0F1014", ["Surface"] = "#16171C", ["SurfaceSolid"] = "#16171C", ["Surface2"] = "#1D1F25",
            ["Surface3"] = "#272A31", ["Stroke"] = "#22242B", ["StrokeStrong"] = "#33363F", ["Text"] = "#EDEDF0", ["Muted"] = "#8E919B",
            ["Faint"] = "#5B5F69", ["Accent"] = "#F07AA0", ["AccentHover"] = "#F48DAE", ["Accent2"] = "#9D8CFF", ["AccentSoft"] = "#1FF07AA0",
            ["Gold"] = "#E8B860", ["Good"] = "#5CC99A", ["GoodSoft"] = "#1F5CC99A", ["Bad"] = "#EF6F6F", ["BadSoft"] = "#1FEF6F6F",
            ["Info"] = "#6FA8F0", ["AccentGradient"] = "#EE7AA0", ["GoldGradient"] = "#E2B15E", ["CoolGradient"] = "#6F94E8",
            ["GreenGradient"] = "#55BE90", ["GlassStroke"] = "#22242B", ["CardFill"] = "#16171C", ["HoverTint"] = "#FFFFFF",
            ["SelectedTint"] = "#0FFFFFFF", ["Thumb"] = "#1FFFFFFF", ["ThumbHover"] = "#40FFFFFF", ["CaptionHover"] = "#12FFFFFF", ["OnAccent"] = "#1A0B11",
        };
        var lite = new Dictionary<string, string>
        {
            ["Bg"] = "#F6F5F3", ["Sidebar"] = "#F6F5F3", ["Surface"] = "#FFFFFF", ["SurfaceSolid"] = "#FFFFFF", ["Surface2"] = "#F0EEEB",
            ["Surface3"] = "#E4E1DD", ["Stroke"] = "#E6E3DF", ["StrokeStrong"] = "#D3CFCA", ["Text"] = "#1C1B1F", ["Muted"] = "#6B6875",
            ["Faint"] = "#9C98A3", ["Accent"] = "#D9487A", ["AccentHover"] = "#E05C89", ["Accent2"] = "#6F5BD9", ["AccentSoft"] = "#1FD9487A",
            ["Gold"] = "#B9831F", ["Good"] = "#2E9E6E", ["GoodSoft"] = "#1F2E9E6E", ["Bad"] = "#D64545", ["BadSoft"] = "#1FD64545",
            ["Info"] = "#3D7FD6", ["AccentGradient"] = "#DD5584", ["GoldGradient"] = "#D19A3A", ["CoolGradient"] = "#4F7FD9",
            ["GreenGradient"] = "#3AA77A", ["GlassStroke"] = "#E6E3DF", ["CardFill"] = "#FFFFFF", ["HoverTint"] = "#000000",
            ["SelectedTint"] = "#0D000000", ["Thumb"] = "#26000000", ["ThumbHover"] = "#4D000000", ["CaptionHover"] = "#12000000", ["OnAccent"] = "#FFFFFF",
        };
        var pal = light ? lite : dark;
        var (d, l) = accent switch
        {
            "mint" => ("#4FD1B5", "#1E9E85"),
            "indigo" => ("#8C9EFF", "#4F5FD9"),
            "amber" => ("#F2B45A", "#C07F15"),
            _ => ("", ""),
        };
        if (d.Length > 0)
        {
            var c = Color.Parse(light ? l : d);
            pal["Accent"] = pal["AccentGradient"] = pal["AccentHover"] = c.ToString();
            pal["AccentSoft"] = Color.FromArgb(0x1F, c.R, c.G, c.B).ToString();
        }
        foreach (var (k, v) in pal) r[k] = new SolidColorBrush(Color.Parse(v));
    }
}
