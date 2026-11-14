using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Matsubakira.Services;

namespace Matsubakira;

/// <summary>
/// Окно для компьютера: заголовок, иконка, своя рамка в Windows, сворачивание в трей.
/// Весь интерфейс — в MainView (он же показывается на телефоне без окна).
/// </summary>
public class MainWindow : Window
{
    public MainView View { get; }
    bool _reallyClose;

    public MainWindow()
    {
        Title = "Kotodama · 言霊 — японский язык";
        Width = 1280; Height = 820; MinWidth = 1020; MinHeight = 680;
        // 1366×768 при масштабе 125–150 % — это ~910×510: минимальный размер не должен быть больше экрана (Д9)
        Opened += (_, _) =>
        {
            if (Shell.TestRun || Screens.ScreenFromWindow(this) is not { } scr) return; // снимки в CI — всегда одного размера
            double sc = scr.Scaling, w = scr.WorkingArea.Width / sc, h = scr.WorkingArea.Height / sc;
            MinWidth = Math.Min(MinWidth, w - 16); MinHeight = Math.Min(MinHeight, h - 16);
            if (Width > w || Height > h) { Width = Math.Min(Width, w); Height = Math.Min(Height, h); }
        };
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = U.B("Bg");
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Kotodama/Assets/app.ico"))); } catch { } // иконка не загрузилась — у окна будет стандартная
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
#if DEBUG
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => a is "--snap" or "--selftest" or "--offscreen" or "--speechaudit" or "--cliptest" or "--perf" or "--rerender"))
        {
            // проверки идут невидимо: окно прозрачное, без рамки, не в панели задач и за краем экрана (снимок рендерится из интерфейса, не с экрана)
            ShowActivated = false; ShowInTaskbar = false; Opacity = 0; SystemDecorations = SystemDecorations.None; Topmost = false;
            Shell.TestRun = true;
            if (args.Contains("--snap") || args.Contains("--cliptest") || args.Contains("--perf") || args.Contains("--rerender")) { Platform.AudioPlayer.Muted = true; ProgressStore.ReadOnly = true; }
            WindowStartupLocation = WindowStartupLocation.Manual; Position = new PixelPoint(-20000, 0);
        }
        // проверка телефонной раскладки на компьютере: размер экрана телефона
        if (Shell.Compact)
        {
            MinWidth = 320; MinHeight = 560; Width = 392; Height = 852;
            // другой размер экрана для проверки: KOTODAMA_PHONE=320x640
            if (Environment.GetEnvironmentVariable("KOTODAMA_PHONE")?.Split('x') is [var pw, var ph] && double.TryParse(pw, out var w) && double.TryParse(ph, out var h)) { Width = w; Height = h; }
        }
        // размер окна для снимков (видео-обзор, скриншоты): KOTODAMA_SIZE=1600x900
        else if (Environment.GetEnvironmentVariable("KOTODAMA_SIZE")?.Split('x') is [var sw, var sh] && double.TryParse(sw, out var w2) && double.TryParse(sh, out var h2)) { Width = w2; Height = h2; }
#endif
        View = new MainView();
        Content = View;
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) View.OnStateChanged(WindowState); };
        Activated += (_, _) => View.UpdateStats();
        // «Сворачивать в трей»: окно прячется, напоминания и слово дня продолжают приходить
        Closing += (_, e) =>
        {
            // только когда закрывает пользователь (не выход из системы) и только если значок в трее есть (Д8)
            if (ProgressStore.P.MinimizeToTray && !_reallyClose && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                if (Tray.Available) { Hide(); Shell.Toast("Kotodama работает в трее", "言"); }
                else WindowState = WindowState.Minimized; // значка нет — окно на панели задач, а не потеряно
            }
        };
    }

    public void Quit() { _reallyClose = true; Close(); }
}
