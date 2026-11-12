using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Matsubakira.Services;
using Matsubakira.Views;
using RadioButton = Matsubakira.Compat.RadioButton;

namespace Matsubakira;

// Тосты, раскрытие карточки, оверлеи, «да/нет», Ctrl+K и интро (часть MainView)
public partial class MainView : UserControl
{
    // ─────────── Тост ───────────
    public void ShowToast(string text, string? jp)
    {
        Dispatcher.UIThread.Post(Sfx.Notify, DispatcherPriority.Background); // после синхронных звуков (достижение, ответ) — колокольчик не накладывается
        _toastText.Text = text;
        _toastJp.Text = jp ?? "";
        _toastJp.IsVisible = !string.IsNullOrEmpty(jp);
        // появление 0,2 с → 2,2 с на экране → исчезновение 0,4 с
        Anim.Run((_toast, "op"), 0, 1, 2800, k => _toast.Opacity = k < 0.07 ? k / 0.07 : k < 0.857 ? 1 : (1 - k) / 0.143, Ease.Linear);
        Anim.Pop(_toast, 0.9);
    }

    /// <summary>Лепестки сакуры отключены в кроссплатформенной версии (дорогой декор); настройка сохраняется.</summary>
    public void SetPetals(bool on) { }

    // ─────────── «Раскрытие» карточки в страницу ───────────
    bool _expanding;
    public void ExpandFrom(Control source, Control page)
    {
        if (_expanding || !source.IsEffectivelyVisible) { Push(page); return; }
        var a = source.TranslatePoint(new Point(0, 0), _root);
        var b = _host.TranslatePoint(new Point(0, 0), _root);
        if (a == null || b == null || _host.Bounds.Width < 1) { Push(page); return; }
        _expanding = true;
        Rect from = new(a.Value, source.Bounds.Size), to = new(b.Value, _host.Bounds.Size);
        var ghost = new Border
        {
            Width = to.Width, Height = to.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(to.X, to.Y, 0, 0), Background = U.B("Surface"), BorderBrush = U.B("StrokeStrong"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), IsHitTestVisible = false, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
        };
        var sc = new ScaleTransform(from.Width / to.Width, from.Height / to.Height);
        var tr = new TranslateTransform(from.X - to.X, from.Y - to.Y);
        ghost.RenderTransform = new TransformGroup { Children = { sc, tr } };
        _root.Children.Add(ghost);
        double sx = sc.ScaleX, sy = sc.ScaleY, tx = tr.X, ty = tr.Y;
        Anim.Run((ghost, "expand"), 0, 1, 300, k =>
        {
            sc.ScaleX = sx + (1 - sx) * k; sc.ScaleY = sy + (1 - sy) * k;
            tr.X = tx * (1 - k); tr.Y = ty * (1 - k);
        }, Ease.QuinticInOut, 0, () =>
        {
            Push(page);
            Anim.Opacity(ghost, 1, 0, 220, 0, () => { _root.Children.Remove(ghost); _expanding = false; });
        });
    }

    // ─────────── Карточка поверх окна (слово дня и т.п.) ───────────
    Grid? _overlay;
    public void ShowOverlay(Control content)
    {
        CloseOverlay();
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        var close = new Button { Content = "", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Classes = { "IconButton" } };
        close.Click += (_, _) => CloseOverlay();
        var card = new Border
        {
            // на телефоне — во всю ширину с полями (минимум 440 не помещался в экран 360)
            Classes = { "Card" }, MinWidth = Shell.Compact ? 0 : 440, MaxWidth = 620, HorizontalAlignment = Shell.Compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = Shell.Compact ? new Thickness(12) : default,
            Padding = Shell.Compact ? new Thickness(18, 16) : new Thickness(30, 22), BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
            Child = new Grid { Children = { content, close } },
        };
        overlay.Children.Add(card);
        overlay.PointerPressed += (_, e) => { if (e.Source == overlay) CloseOverlay(); };
        _overlay = overlay;
        _root.Children.Add(overlay);
        Anim.FadeUp(card, 0, 12, 260);
    }

    public void CloseOverlay() { if (_overlay != null) { _root.Children.Remove(_overlay); _overlay = null; } }

    // ─────────── Диалог «да/нет» ───────────
    public Task<bool> Confirm(string text, string yes, string no)
    {
        var tcs = new TaskCompletionSource<bool>();
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        void Done(bool r) { _root.Children.Remove(overlay); tcs.TrySetResult(r); }
        var card = new Border
        {
            Classes = { "Card" }, Width = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(26, 22),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
            Child = U.Stack(
                U.T("Kotodama", "Overline"),
                U.T(text, "Body", 15).M(0, 8, 0, 20),
                U.Row(10, U.Btn(yes, () => Done(true), "Primary"), U.Btn(no, () => Done(false))).Also(r => r.HorizontalAlignment = HorizontalAlignment.Right)),
        };
        overlay.Children.Add(card);
        overlay.PointerPressed += (_, e) => { if (e.Source == overlay) Done(false); };
        overlay.KeyDown += (_, e) => { if (e.Key == Key.Escape) Done(false); else if (e.Key == Key.Enter) Done(true); };
        _root.Children.Add(overlay);
        Anim.FadeUp(card, 0, 10, 220);
        return tcs.Task;
    }

    // ─────────── Ctrl+K ───────────
    Controls.Palette? _palette;
    public void OpenPalette()
    {
        if (_palette != null) return;
        _palette = new Controls.Palette(() => { _root.Children.Remove(_palette!); _palette = null; });
        _root.Children.Add(_palette);
    }

    // ─────────── Интро ───────────
    bool _introClosing;

    /// <summary>Черты 言霊 прорисовываются кистью, затем появляется название, и экран растворяется (~3 с, пропуск — клик/клавиша).</summary>
    void PlayIntro()
    {
        var cl = Platform.Hooks.Args ?? Environment.GetCommandLineArgs();
        if (!cl.Contains("--intro") && (!ProgressStore.P.Intro || cl.Any(a => a.StartsWith("--")))) return;
        // телефон: заставка только при самом первом запуске — дальше сразу к делу (каждый раз это ~4 с ожидания)
        if (Shell.Compact && !cl.Contains("--intro") && !ProgressStore.P.Flags.Add("intro-seen")) return;
        _intro.IsVisible = true;
        _intro.Opacity = 1;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var s1 = new Controls.StrokeView("言", 150, plain: true, ink: U.B("Accent"), thick: 4.4);
        var s2 = new Controls.StrokeView("霊", 150, plain: true, ink: U.B("Text"), thick: 4.4) { Margin = new Thickness(14, 0, 0, 0) };
        row.Children.Add(s1); row.Children.Add(s2);
        stack.Children.Add(row);
        var title = new TextBlock { Text = "Kotodama", FontSize = 30, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0), Opacity = 0 };
        var sub = new TextBlock { Text = "言霊 — душа слова · японский шаг за шагом", FontSize = 14, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Opacity = 0 };
        var line = new Border { Height = 2, Width = 0, CornerRadius = new CornerRadius(1), Background = U.B("Accent"), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(title); stack.Children.Add(sub); stack.Children.Add(line);
        _intro.Children.Clear();
        _intro.Children.Add(stack);
        _intro.PointerPressed += (_, _) => CloseIntro(fast: true);

        double end = Math.Max(s1.Play(2.6, 250), s2.Play(3.2, 550));
        Anim.FadeUp(title, (int)Math.Max(0, end - 350), 14, 520);
        Anim.FadeUp(sub, (int)Math.Max(0, end - 180), 10, 520);
        Anim.Run((line, "w"), 0, 120, 600, v => line.Width = v, Ease.QuinticOut, (int)end);
        DispatcherTimer.RunOnce(() => CloseIntro(fast: false), TimeSpan.FromMilliseconds(end + 900));
    }

    void CloseIntro(bool fast)
    {
        if (_introClosing || !_intro.IsVisible) return;
        _introClosing = true;
        if (_intro.Children.Count > 0 && _intro.Children[0] is Control st)
        {
            var (s, _) = Anim.Transforms(st);
            Anim.Run((s, "sc"), 1, 1.05, 520, v => { s.ScaleX = v; s.ScaleY = v; });
        }
        Anim.Opacity(_intro, 1, 0, fast ? 220 : 520, 0, () => { _intro.IsVisible = false; _intro.Children.Clear(); }, Ease.CubicInOut);
        Anim.Opacity(_host, 0, 1, 500);
    }
}
