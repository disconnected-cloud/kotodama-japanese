using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Matsubakira.Services;

/// <summary>
/// Значок в трее и системные уведомления.
/// Windows — Shell_NotifyIcon (всплывающие уведомления, клик открывает окно), иконка со счётчиком карточек.
/// Linux — значок Avalonia (StatusNotifier) с меню, уведомления через notify-send.
/// </summary>
public static class Tray
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int n);
    [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] data, int size, bool icon, int ver, int cx, int cy, int flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIIF_USER = 4, WM_APP_TRAY = 0x8001;
    const int NIN_BALLOONUSERCLICK = 0x405, WM_LBUTTONUP = 0x202;

    static NOTIFYICONDATA _d;
    static bool _added;
    static Window? _win;
    static TrayIcon? _linuxIcon;
    static IntPtr _hIcon;
    /// <summary>Куда перейти по клику на последнее уведомление.</summary>
    static string? _clickTarget;

    public static void Init(Window w)
    {
        _win = w;
        if (OperatingSystem.IsWindows()) InitWindows(w);
        else InitLinux();
        w.Closed += (_, _) => Remove();
    }

    static void InitWindows(Window w)
    {
        var hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        Win32Properties.AddWndProcHookCallback(w, Hook);
        ExtractIconEx(Environment.ProcessPath!, 0, out _, out var small, 1);
        _d = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY, hIcon = small, szTip = "Kotodama — японский", szInfo = "", szInfoTitle = "",
        };
        _added = Shell_NotifyIcon(NIM_ADD, ref _d);
    }

    static void InitLinux()
    {
        try
        {
            var menu = new NativeMenu();
            void Item(string text, Action a) { var mi = new NativeMenuItem(text); mi.Click += (_, _) => a(); menu.Items.Add(mi); }
            Item("Открыть Kotodama", ShowWindow);
            Item("Слово дня", () => { ShowWindow(); WordOfDay.Show(force: true); });
            Item("Повторение", () => { ShowWindow(); Shell.Go("review"); });
            menu.Items.Add(new NativeMenuItemSeparator());
            Item("Выход", () => Shell.Main.Quit());
            _linuxIcon = new TrayIcon { ToolTipText = "Kotodama — японский", Menu = menu, IsVisible = true };
            _linuxIcon.Clicked += (_, _) => ShowWindow();
            if (Application.Current != null) TrayIcon.SetIcons(Application.Current, new TrayIcons { _linuxIcon });
        }
        catch { _linuxIcon = null; }
    }

    /// <summary>Новая картинка значка (PNG) и подсказка с числом карточек.</summary>
    public static void SetIcon(byte[] png, int due)
    {
        string tip = due > 0 ? $"Kotodama — {due} {MainView.Plural(due, "карточка", "карточки", "карточек")} к повторению" : "Kotodama — японский";
        if (OperatingSystem.IsWindows())
        {
            if (!_added) return;
            var h = CreateIconFromResourceEx(png, png.Length, true, 0x00030000, 32, 32, 0);
            if (h == IntPtr.Zero) return;
            _d.uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE;
            _d.hIcon = h; _d.szTip = tip;
            Shell_NotifyIcon(NIM_MODIFY, ref _d);
            if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
            _hIcon = h;
        }
        else if (_linuxIcon != null)
        {
            _linuxIcon.Icon = new WindowIcon(new MemoryStream(png));
            _linuxIcon.ToolTipText = tip;
        }
    }

    public static void Notify(string title, string text, string? clickTarget = null, bool force = false)
    {
        if (!force && !ProgressStore.P.Reminders) return;
        _clickTarget = clickTarget;
        if (!OperatingSystem.IsWindows())
        {
            Sfx.Reminder();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notify-send", new[] { "-a", "Kotodama", "-i", "kotodama", title, text }) { UseShellExecute = false }); } catch { }
            return;
        }
        if (!_added) return;
        _d.uFlags = NIF_INFO | NIF_ICON | NIF_TIP | NIF_MESSAGE;
        _d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _d.szInfo = text.Length > 255 ? text[..255] : text;
        _d.dwInfoFlags = NIIF_USER | 0x10; // NIIF_NOSOUND: вместо системного «дзынь» — свой мягкий колокольчик
        Sfx.Reminder();
        Shell_NotifyIcon(NIM_MODIFY, ref _d);
    }

    public static void Remove()
    {
        if (_added) { Shell_NotifyIcon(NIM_DELETE, ref _d); _added = false; }
        if (_linuxIcon != null) { _linuxIcon.IsVisible = false; _linuxIcon.Dispose(); _linuxIcon = null; }
    }

    public static void ShowWindow()
    {
        if (_win == null) return;
        _win.Show();
        if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
        _win.Activate();
    }

    static IntPtr Hook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY && ((int)lParam == NIN_BALLOONUSERCLICK || (int)lParam == WM_LBUTTONUP) && _win != null)
        {
            ShowWindow();
            if ((int)lParam == NIN_BALLOONUSERCLICK)
            {
                if (_clickTarget == "word") WordOfDay.Show(force: true);
                else if (ProgressStore.P.Cards.Values.Any(c => c.Due <= DateTime.Now)) Shell.Go("review");
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ─────────── Логика напоминаний ───────────
    static DateTime _lastReminder = DateTime.MinValue;

    /// <summary>Раз в минуту: слово дня по расписанию; напоминания — только если окно не в фокусе и не чаще раза в 2 часа.</summary>
    public static void Tick()
    {
        if (_win == null) return;
        WordOfDay.Tick();
        Badge.Update(_win);
        if (_win.IsActive || (DateTime.Now - _lastReminder).TotalHours < 2) return;
        var p = ProgressStore.P;
        int due = p.Cards.Values.Count(c => c.Due <= DateTime.Now);
        if (DateTime.Now.Hour >= 20 && p.Streak > 0 && ProgressStore.TodayXp == 0)
        {
            Notify("Серия под угрозой 🔥", $"Серия {p.Streak} {MainView.Plural(p.Streak, "день", "дня", "дней")} сгорит в полночь. Хватит 5 минут!");
            _lastReminder = DateTime.Now;
        }
        else if (due >= 10)
        {
            Notify("Пора повторить", $"{due} карточек ждут повторения — это займёт пару минут.");
            _lastReminder = DateTime.Now;
        }
    }
}
