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
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = U.B("Bg");
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Kotodama/Assets/app.ico"))); } catch { }
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
#if DEBUG
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => a is "--snap" or "--selftest" or "--offscreen" or "--speechaudit"))
        {
            ShowActivated = false; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual; Position = new PixelPoint(-20000, 0);
        }
        // проверка телефонной раскладки на компьютере: размер экрана телефона
        if (Shell.Compact) { MinWidth = 320; MinHeight = 560; Width = 392; Height = 852; }
#endif
        View = new MainView();
        Content = View;
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) View.OnStateChanged(WindowState); };
        Activated += (_, _) => View.UpdateStats();
        // «Сворачивать в трей»: окно прячется, напоминания и слово дня продолжают приходить
        Closing += (_, e) =>
        {
            if (ProgressStore.P.MinimizeToTray && !_reallyClose && !e.IsProgrammatic) { e.Cancel = true; Hide(); Shell.Toast("Kotodama работает в трее", "言"); }
        };
    }

    public void Quit() { _reallyClose = true; Close(); }
}
