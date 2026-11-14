using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Services;

namespace Matsubakira.Controls;

/// <summary>Большая круглая кнопка микрофона: векторный значок строго по центру, ореол реагирует на громкость,
/// во время распознавания — вращающаяся дуга. Все слои в одной сетке одного размера, поэтому ничего не съезжает.</summary>
public sealed class MicButton : Grid
{
    public enum Mode { Idle, Recording, Thinking }

    const double D = 88;          // диаметр кнопки
    const double Box = 150;       // место под ореол
    readonly Ellipse _halo, _halo2;
    readonly ScaleTransform _haloS = new(1, 1), _halo2S = new(1, 1), _pressS = new(1, 1);
    readonly Border _btn;
    readonly Control _micGlyph;
    readonly Border _stopGlyph;
    readonly Arc _spin;
    readonly RotateTransform _spinR = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    Recorder? _rec;
    double _smooth;
    public Mode State { get; private set; }
    public event Action? Click;

    public MicButton()
    {
        Width = Box; Height = Box;
        HorizontalAlignment = HorizontalAlignment.Center;
        var accent = ((ISolidColorBrush)U.B("Accent")).Color;

        _halo = new Ellipse { Width = D, Height = D, Fill = new SolidColorBrush(accent, 0.18), RenderTransform = _haloS, RenderTransformOrigin = RelativePoint.Center, Opacity = 0, IsHitTestVisible = false };
        _halo2 = new Ellipse { Width = D, Height = D, Fill = new SolidColorBrush(accent, 0.10), RenderTransform = _halo2S, RenderTransformOrigin = RelativePoint.Center, Opacity = 0, IsHitTestVisible = false };

        _micGlyph = Art.Glyph("mic", 34, new SolidColorBrush(Color.FromRgb(0x1A, 0x0B, 0x11)));
        _micGlyph.HorizontalAlignment = HorizontalAlignment.Center; _micGlyph.VerticalAlignment = VerticalAlignment.Center;
        _micGlyph.Margin = new Thickness(0, 1.5, 0, 0); // у рисунка микрофона ножка внизу — оптически центр чуть ниже
        _stopGlyph = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x0B, 0x11)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };

        _btn = new Border
        {
            Width = D, Height = D, CornerRadius = new CornerRadius(D / 2), Cursor = Cursors.Hand,
            Background = U.B("AccentGradient"),
            BoxShadow = BoxShadows.Parse($"0 10 28 0 #{0x66:X2}{accent.R:X2}{accent.G:X2}{accent.B:X2}"),
            Child = new Grid { Children = { _micGlyph, _stopGlyph } },
            RenderTransform = _pressS, RenderTransformOrigin = RelativePoint.Center,
        };
        _spin = new Arc { Width = D + 14, Height = D + 14, Stroke = U.B("Accent"), StrokeThickness = 3, StrokeLineCap = PenLineCap.Round, StartAngle = 0, SweepAngle = 80, Opacity = 0, IsHitTestVisible = false, RenderTransform = _spinR, RenderTransformOrigin = RelativePoint.Center };
        foreach (var c in new Control[] { _halo2, _halo, _spin, _btn }) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; Children.Add(c); }

        _btn.PointerEntered += (_, _) => { if (State == Mode.Idle) Anim.Run((_pressS, "s"), _pressS.ScaleX, 1.05, 180, v => { _pressS.ScaleX = _pressS.ScaleY = v; }); };
        _btn.PointerExited += (_, _) => Anim.Run((_pressS, "s"), _pressS.ScaleX, 1, 220, v => { _pressS.ScaleX = _pressS.ScaleY = v; });
        _btn.PointerPressed += (_, _) => Anim.Run((_pressS, "s"), _pressS.ScaleX, 0.94, 90, v => { _pressS.ScaleX = _pressS.ScaleY = v; });
        _btn.OnClick(() => { Anim.Run((_pressS, "s"), _pressS.ScaleX, 1, 260, v => { _pressS.ScaleX = _pressS.ScaleY = v; }, Ease.BackOut(0.8)); Click?.Invoke(); });
        ToolTip.SetTip(_btn, "Говорить (пробел)");

        _timer.Tick += (_, _) => Tick();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    void Tick()
    {
        double t = Environment.TickCount64 / 1000.0;
        if (State == Mode.Recording)
        {
            // ореол дышит с голосом: тишина — лёгкая пульсация, речь — расширение
            double lvl = Math.Min(1, (_rec?.Level ?? 0) * 1.6);
            _smooth = _smooth * 0.6 + lvl * 0.4;
            double idle = 0.03 * (1 + Math.Sin(t * 3));
            _haloS.ScaleX = _haloS.ScaleY = 1.08 + idle + _smooth * 0.32;
            _halo2S.ScaleX = _halo2S.ScaleY = 1.18 + idle * 1.5 + _smooth * 0.5;
        }
        else if (State == Mode.Thinking) _spinR.Angle = (t * 300) % 360;
    }

    /// <summary>Переключить состояние; rec — источник громкости для ореола во время записи.</summary>
    public void Set(Mode m, Recorder? rec = null)
    {
        State = m;
        _rec = rec;
        bool recOn = m == Mode.Recording;
        _micGlyph.IsVisible = !recOn;
        _stopGlyph.IsVisible = recOn;
        _btn.Background = recOn ? U.B("GoldGradient") : U.B("AccentGradient");
        _btn.Opacity = m == Mode.Thinking ? 0.75 : 1;
        Anim.Opacity(_halo, _halo.Opacity, recOn ? 1 : 0, 220);
        Anim.Opacity(_halo2, _halo2.Opacity, recOn ? 1 : 0, 220);
        Anim.Opacity(_spin, _spin.Opacity, m == Mode.Thinking ? 1 : 0, 200);
        ToolTip.SetTip(_btn, recOn ? "Закончить (пробел)" : "Говорить (пробел)");
        if (m == Mode.Idle) { _timer.Stop(); _haloS.ScaleX = _haloS.ScaleY = _halo2S.ScaleX = _halo2S.ScaleY = 1; }
        else _timer.Start();
    }
}
