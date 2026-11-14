using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Быстрый словарь (8.1): выдели японское слово в любой программе и нажми Ctrl+Alt+J — рядом с курсором всплывёт
/// окошко с чтением, переводом и кнопкой «Учить». Выделенный текст берётся через буфер обмена (программа сама
/// нажимает Ctrl+C), прежнее содержимое буфера возвращается. Только Windows.
/// </summary>
public static class QuickDict
{
    const int HotkeyId = 0x4B44, WM_HOTKEY = 0x0312, MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, int mods, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    static Window? _main, _popup;

    public static void Init(Window main)
    {
        if (!OperatingSystem.IsWindows() || main.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        _main = main;
        if (!RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'J')) return; // сочетание занято другой программой
        Avalonia.Controls.Win32Properties.AddWndProcHookCallback(main, (IntPtr h, uint msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_HOTKEY && (int)w == HotkeyId) { handled = true; _ = Lookup(); }
            return IntPtr.Zero;
        });
        main.Closed += (_, _) => UnregisterHotKey(hwnd, HotkeyId);
    }

    static async Task Lookup()
    {
        var clip = TopLevel.GetTopLevel(_main)?.Clipboard;
        if (clip == null) return;
        string? before = null;
        try { before = await clip.GetTextAsync(); } catch { } // в буфере не текст — вернуть нечего
        // отпустить Ctrl/Alt сочетания и скопировать выделенное в активной программе
        foreach (byte k in new byte[] { 0x11, 0x12, 0x4A }) keybd_event(k, 0, 2, IntPtr.Zero);
        keybd_event(0x11, 0, 0, IntPtr.Zero); keybd_event(0x43, 0, 0, IntPtr.Zero); keybd_event(0x43, 0, 2, IntPtr.Zero); keybd_event(0x11, 0, 2, IntPtr.Zero);
        await Task.Delay(180);
        string text = "";
        try { text = (await clip.GetTextAsync() ?? "").Trim(); } catch { } // буфер занят — покажем подсказку ниже
        if (before != null && before != text) try { await clip.SetTextAsync(before); } catch { } // не вышло вернуть — не страшно
        if (text.Length is 0 or > 40) text = before?.Trim() ?? "";
        await JDict.LoadAsync();
        Show(text.Length is > 0 and <= 40 ? JDict.Search(text, 3) : new List<DictEntry>(), text);
    }

    static void Show(List<DictEntry> found, string query)
    {
        _popup?.Close();
        var sp = new StackPanel { Spacing = 8, Margin = new Thickness(16), MaxWidth = 380 };
        if (found.Count == 0)
            sp.Children.Add(U.T(query.Length == 0 ? "Выдели японское слово и нажми Ctrl+Alt+J." : $"«{query}» — нет в словаре.", "Body"));
        foreach (var e in found)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(U.Jp(e.Kanji.Length > 0 ? e.Kanji.Split('、')[0] : e.Kana, 26, U.B("Text")));
            if (e.Kanji.Length > 0) row.Children.Add(U.Jp(e.Kana, 15, U.B("Accent")));
            row.Children.Add(U.T(e.Ru, "Body", 13).Also(t => t.TextWrapping = TextWrapping.Wrap));
            var learn = U.Btn(JDict.InStudy(e) ? "Уже в карточках" : "Учить", () => { if (JDict.AddToStudy(e)) Shell.Toast("Слово добавлено в карточки", "+"); }, "Primary", "");
            learn.IsEnabled = !JDict.InStudy(e);
            learn.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(U.Row(8, learn, U.Btn("Послушать", () => Speech.Say(e.Kana), "", "")));
            sp.Children.Add(row);
        }
        var w = new Window
        {
            SystemDecorations = SystemDecorations.BorderOnly, Topmost = true, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false, Background = U.B("Surface"), Content = sp, Title = "Kotodama — словарь",
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        if (GetCursorPos(out var p)) w.Position = new PixelPoint(p.X + 12, p.Y + 16);
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.Deactivated += (_, _) => w.Close();
        _popup = w;
        w.Show();
        w.Activate();
    }
}
