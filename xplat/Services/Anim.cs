using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Matsubakira.Services;

// Плавности и движок анимаций: простые твины на таймере
/// <summary>Функции сглаживания для анимаций.</summary>
public static class Ease
{
    public static double Linear(double t) => t;
    public static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double QuinticOut(double t) => 1 - Math.Pow(1 - t, 5);
    public static double QuadIn(double t) => t * t;
    public static double CubicInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double QuinticInOut(double t) => t < 0.5 ? 16 * Math.Pow(t, 5) : 1 - Math.Pow(-2 * t + 2, 5) / 2;
    public static double SineInOut(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;
    public static Func<double, double> BackOut(double amp = 0.4) => t =>
    {
        double c1 = 1.70158 * amp, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    };
}

/// <summary>
/// Небольшие анимации: только прозрачность и трансформации (дёшево, без перерасчёта разметки).
/// Свой лёгкий движок: таймер работает, только пока идёт хотя бы одна анимация, — в простое 0% CPU.
/// </summary>
public static class Anim
{
    sealed class Tween
    {
        public long Start; public int Delay, Ms; public double From, To; public Action<double> Set = null!;
        public Func<double, double> Ease = Services.Ease.CubicOut; public Action? Done; public bool Forever, AutoReverse;
    }

    static readonly Dictionary<object, Tween> Active = new();
    static DispatcherTimer? _timer;

    static long Now => Environment.TickCount64;

    /// <summary>Анимирует значение от from до to. key — «слот» (элемент+свойство): новая анимация в том же слоте заменяет старую.</summary>
    public static void Run(object key, double from, double to, int ms, Action<double> set, Func<double, double>? ease = null, int delay = 0, Action? done = null, bool forever = false, bool autoReverse = false)
    {
        var tw = new Tween { Start = Now, Delay = delay, Ms = Math.Max(1, ms), From = from, To = to, Set = set, Ease = ease ?? Ease.CubicOut, Done = done, Forever = forever, AutoReverse = autoReverse };
        Active[key] = tw;
        if (delay <= 0) set(from);
        EnsureTimer();
    }

    public static void Stop(object key) => Active.Remove(key);

    static void EnsureTimer()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Tick());
        }
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>Для замера простоя: сколько кадров анимаций было и какие сейчас активны.</summary>
    public static int Ticks;
    public static string ActiveKeys => string.Join("; ", Active.Keys.Take(12).Select(k => k.ToString()));

    static void Tick()
    {
        Ticks++;
        long now = Now;
        List<(object, Tween)>? finished = null;
        foreach (var (key, tw) in Active.ToArray())
        {
            long el = now - tw.Start - tw.Delay;
            if (el < 0) continue;
            double k = (double)el / tw.Ms;
            if (tw.Forever)
            {
                // элемент ушёл со страницы — бесконечная анимация больше не держит его и таймер (Д6)
                if (el > 1000 && key is System.Runtime.CompilerServices.ITuple { Length: > 0 } tk && tk[0] is Visual v && TopLevel.GetTopLevel(v) == null)
                { (finished ??= new()).Add((key, tw)); continue; }
                double cyc = k % (tw.AutoReverse ? 2 : 1);
                if (tw.AutoReverse && cyc > 1) cyc = 2 - cyc;
                tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(Math.Min(1, cyc)));
                continue;
            }
            if (k >= 1) { tw.Set(tw.To); (finished ??= new()).Add((key, tw)); continue; }
            tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(k));
        }
        if (finished != null)
            foreach (var (key, tw) in finished)
            {
                if (Active.TryGetValue(key, out var cur) && cur == tw) Active.Remove(key);
                tw.Done?.Invoke();
            }
        if (Active.Count == 0) _timer?.Stop();
    }

    public static (ScaleTransform s, TranslateTransform t) Transforms(Visual el)
    {
        if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0)
            return (s0, t0);
        var s = new ScaleTransform(1, 1); var t = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { s, t } };
        el.RenderTransformOrigin = RelativePoint.Center;
        return (s, t);
    }

    public static void Opacity(Visual el, double from, double to, int ms, int delay = 0, Action? done = null, Func<double, double>? ease = null) =>
        Run((el, "op"), from, to, ms, v => el.Opacity = v, ease, delay, done);

    /// <summary>
    /// Облегчённый режим: без каскадов и плавных появлений — каждое из них перерисовывает экран ~25 раз.
    /// Сам включается на слабом телефоне (≤ 4 ядер или &lt; 3 ГБ памяти), в настройках можно выбрать вручную.
    /// </summary>
    public static bool Lite;

    public static bool LowEndDevice =>
        OperatingSystem.IsAndroid() && (Environment.ProcessorCount <= 4 || GC.GetGCMemoryInfo().TotalAvailableMemoryBytes is > 0 and < 3L << 30);

    public static void ApplyMotion(int motion) => Lite = motion == 2 || (motion == 0 && LowEndDevice);

    public static void FadeUp(Visual el, int delay = 0, double dy = 16, int ms = 380)
    {
        if (Lite) return;
        var (_, t) = Transforms(el);
        el.Opacity = 0;
        t.Y = dy;
        Run((el, "op"), 0, 1, ms, v => el.Opacity = v, null, delay);
        Run((t, "y"), dy, 0, ms, v => t.Y = v, null, delay);
    }

    /// <summary>Каскадное появление детей панели.</summary>
    public static void Stagger(Panel p, int step = 40, int start = 0, int max = 24)
    {
        if (Shell.Compact) max = Math.Min(max, 6); // телефон: анимируем только то, что на первом экране
        int i = 0;
        foreach (var c in p.Children)
        {
            if (i < max) FadeUp(c, start + i * step, 14, 360);
            i++;
        }
    }

    public static void Pop(Visual el, double from = 0.85)
    {
        if (Lite) return;
        var (s, _) = Transforms(el);
        Run((s, "sc"), from, 1, 380, v => { s.ScaleX = v; s.ScaleY = v; }, Ease.BackOut());
    }

    public static void Shake(Visual el)
    {
        if (Lite) return; // «без анимаций» — и без тряски (Д7)
        var (_, t) = Transforms(el);
        double[] xs = { 0, -10, 9, -7, 5, -3, 0 };
        Run((t, "x"), 0, 1, 380, k =>
        {
            double p = k * (xs.Length - 1); int i = Math.Min((int)p, xs.Length - 2);
            t.X = xs[i] + (xs[i + 1] - xs[i]) * (p - i);
        }, Ease.Linear);
    }

    /// <summary>«3D»-переворот карточки: сжатие по X, смена содержимого, разворот.</summary>
    public static void Flip(Visual el, Action midpoint)
    {
        var (s, _) = Transforms(el);
        Run((s, "sx"), 1, 0, 140, v => s.ScaleX = v, Ease.QuadIn, 0, () =>
        {
            midpoint();
            Run((s, "sx"), 0, 1, 220, v => s.ScaleX = v, Ease.BackOut());
        });
    }

    public static void CountUp(TextBlock tb, int to, string suffix = "", int ms = 700)
    {
        if (Lite || to == 0) { tb.Text = to + suffix; return; }
        Run((tb, "count"), 0, to, ms, v => tb.Text = ((int)Math.Round(v)).ToString() + suffix);
    }
}
