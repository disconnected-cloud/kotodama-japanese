using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Matsubakira.Services;

/// <summary>
/// Напоминания через системные уведомления Windows (иконка в трее + всплывающая подсказка).
/// Без WinForms: прямой вызов Shell_NotifyIcon. Клик по уведомлению разворачивает окно.
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

    const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIIF_USER = 4, WM_APP_TRAY = 0x8001;
    const int NIN_BALLOONUSERCLICK = 0x405, WM_LBUTTONUP = 0x202;
    static NOTIFYICONDATA _d;
    static bool _added;
    static Window? _win;

    public static void Init(Window w)
    {
        _win = w;
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        ExtractIconEx(Environment.ProcessPath!, 0, out _, out var small, 1);
        _d = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY, hIcon = small, szTip = "Kotodama — японский", szInfo = "", szInfoTitle = "",
        };
        _added = Shell_NotifyIcon(NIM_ADD, ref _d);
        w.Closed += (_, _) => Remove();
    }

    public static void Notify(string title, string text)
    {
        if (!_added || !ProgressStore.P.Reminders) return;
        _d.uFlags = NIF_INFO | NIF_ICON | NIF_TIP | NIF_MESSAGE;
        _d.szInfoTitle = title; _d.szInfo = text; _d.dwInfoFlags = NIIF_USER;
        Shell_NotifyIcon(NIM_MODIFY, ref _d);
    }

    public static void Remove() { if (_added) { Shell_NotifyIcon(NIM_DELETE, ref _d); _added = false; } }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY && ((int)lParam == NIN_BALLOONUSERCLICK || (int)lParam == WM_LBUTTONUP) && _win != null)
        {
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
            if ((int)lParam == NIN_BALLOONUSERCLICK && ProgressStore.P.Cards.Values.Any(c => c.Due <= DateTime.Now)) Shell.Go("review");
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ─────────── Логика напоминаний ───────────
    static DateTime _lastReminder = DateTime.MinValue;

    /// <summary>Вызывается раз в несколько минут: напоминает, только если окно не в фокусе и не чаще раза в 2 часа.</summary>
    public static void Tick()
    {
        if (_win == null || _win.IsActive || (DateTime.Now - _lastReminder).TotalHours < 2) return;
        var p = ProgressStore.P;
        int due = p.Cards.Values.Count(c => c.Due <= DateTime.Now);
        if (DateTime.Now.Hour >= 20 && p.Streak > 0 && ProgressStore.TodayXp == 0)
        {
            Notify("Серия под угрозой 🔥", $"Серия {p.Streak} {Pl(p.Streak)} сгорит в полночь. Хватит 5 минут!");
            _lastReminder = DateTime.Now;
        }
        else if (due >= 10)
        {
            Notify("Пора повторить", $"{due} карточек ждут повторения — это займёт пару минут.");
            _lastReminder = DateTime.Now;
        }
    }

    static string Pl(int n) => (n % 100 is >= 11 and <= 14) ? "дней" : (n % 10) switch { 1 => "день", 2 or 3 or 4 => "дня", _ => "дней" };
}
