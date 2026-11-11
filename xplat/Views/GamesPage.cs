using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using CheckBox = Matsubakira.Compat.CheckBox;
using ToggleButton = Matsubakira.Compat.ToggleButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

static class GameUi
{
    public static int Best(string key) => ProgressStore.P.GameBest.TryGetValue(key, out var v) ? v : 0;
    public static bool SetBest(string key, int score)
    {
        if (score <= Best(key)) return false;
        ProgressStore.P.GameBest[key] = score; ProgressStore.Notify(); return true;
    }

    public static Control Over(string title, int score, string bestKey, Action again)
    {
        bool record = SetBest(bestKey, score);
        ProgressStore.AddXp(Math.Clamp(score / 20, 2, 30));
        Sfx.Done();
        var sp = U.Stack(
            Controls.Art.Mascot(130, record ? Controls.Mood.Joy : score > 0 ? Controls.Mood.Happy : Controls.Mood.Think).Center().M(0, 0, 0, 8),
            U.Jp(record ? "新記録！" : "お疲れ様！", 36, U.B("Accent"), true).Center(),
            U.T(record ? "Новый рекорд!" : title, "H2").Center().M(0, 8, 0, 0),
            U.T(score.ToString(), "H1", 56).Center().M(0, 10, 0, 0),
            U.T($"Рекорд: {Best(bestKey)}", "Muted").Center(),
            U.Row(10, U.Btn("Ещё раз", again, "Primary", ""), U.Btn("Выйти", Shell.Back, "", "")).Center().M(0, 20, 0, 0));
        var card = U.Card(sp, new Thickness(40, 30, 40, 30));
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
        Anim.Pop(card, 0.85);
        if (record) card.Loaded += (_, _) => { if (card.Parent is Panel p) Results.Confetti(p); };
        return card;
    }
}

/// <summary>Кана-дождь: знаки падают, печатай чтение латиницей. Цикл анимации работает только во время игры.</summary>
public sealed class KanaRainGame : Grid, IPage
{
    public string Title => "Кана-дождь";
    readonly bool _kata;
    readonly Canvas _sky = new() { ClipToBounds = true };
    readonly TextBox _input = new() { Width = 280, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    readonly TextBlock _hud = U.T("", "H3", 15);
    readonly List<(TextBlock tb, KanaCell cell, double y, double speed)> _drops = new();
    int _score, _lives, _combo;
    double _spawn, _level;
    long _last;
    bool _running;
    static readonly Dictionary<string, string[]> Alt = new() { ["shi"] = new[] { "si" }, ["chi"] = new[] { "ti" }, ["tsu"] = new[] { "tu" }, ["fu"] = new[] { "hu" }, ["ji"] = new[] { "zi" }, ["wo"] = new[] { "o" }, ["n"] = new[] { "nn" } };

    public KanaRainGame(bool katakana)
    {
        _kata = katakana;
        Margin = new Thickness(40, 8, 40, 20);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_hud);
        var field = new Border { Classes = { "Card" }, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 12), Child = _sky, ClipToBounds = true };
        Children.Add(field.RowAt(1));
        Children.Add(U.Stack(_input, U.T("Печатай чтение (ka, shi, tsu…) — знак исчезнет. Не дай ему упасть!", "Muted").Center().M(0, 6, 0, 0)).RowAt(2));
        _input.TextChanged += (_, _) => Check();
        Unloaded += (_, _) => StopLoop();
        Loaded += (_, _) => Start();
    }

    void Start()
    {
        foreach (var d in _drops) _sky.Children.Remove(d.tb);
        _drops.Clear();
        if (Children.Count > 3) Children.RemoveAt(3);
        _score = 0; _lives = 3; _combo = 0; _spawn = 0; _level = 1;
        _input.Text = ""; _input.IsEnabled = true; _input.Focus();
        _last = Environment.TickCount64;
        if (!_running) { _frame.Tick -= Tick; _frame.Tick += Tick; _frame.Start(); _running = true; }
        Hud();
    }

    // Кадр игры ~60 раз в секунду, только пока идёт партия
    readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    void StopLoop() { if (_running) { _frame.Stop(); _running = false; } }

    void Hud() => _hud.Text = $"Очки: {_score}    Комбо ×{Math.Max(1, _combo)}    Жизни: {new string('♥', _lives)}{new string('♡', 3 - _lives)}    Уровень {(int)_level}    Рекорд: {GameUi.Best(Key)}";
    string Key => _kata ? "rain-k" : "rain-h";

    void Tick(object? s, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = Math.Min(0.05, (now - _last) / 1000.0);
        _last = now;
        double h = _sky.Bounds.Height, w = _sky.Bounds.Width;
        if (h <= 0) return;
        _level += dt / 20;                    // каждые 20 секунд быстрее
        _spawn -= dt;
        if (_spawn <= 0)
        {
            _spawn = Math.Max(0.55, 1.8 - _level * 0.25);
            var pool = Kana.All.Where(c => c.Group == Kana.Basic || (_level > 3 && c.Group == Kana.Dakuten)).ToList();
            var cell = pool[U.Rng.Next(pool.Count)];
            var tb = new TextBlock { Text = cell.Get(_kata), FontSize = 34, FontFamily = U.R<FontFamily>("JpFont"), Foreground = U.B("Text") };
            Canvas.SetLeft(tb, 20 + U.Rng.NextDouble() * Math.Max(10, w - 70));
            _sky.Children.Add(tb);
            _drops.Add((tb, cell, -40, 40 + _level * 18 + U.Rng.NextDouble() * 20));
        }
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.y += d.speed * dt;
            _drops[i] = d;
            Canvas.SetTop(d.tb, d.y);
            if (d.y > h - 30) d.tb.Foreground = U.B("Bad");
            if (d.y > h)
            {
                _sky.Children.Remove(d.tb); _drops.RemoveAt(i);
                _lives--; _combo = 0; Sfx.Bad();
                Shell.Toast($"{d.cell.Get(_kata)} = {d.cell.Romaji}", d.cell.Get(_kata));
                Hud();
                if (_lives <= 0) { GameOver(); return; }
            }
        }
    }

    void Check()
    {
        var t = _input.Text.Trim().ToLowerInvariant();
        if (t.Length == 0) return;
        int hit = _drops.FindIndex(d => d.cell.Romaji == t || (Alt.TryGetValue(d.cell.Romaji, out var a) && a.Contains(t)));
        if (hit < 0)
        {
            if (!_drops.Any(d => d.cell.Romaji.StartsWith(t))) { _input.Text = ""; }
            return;
        }
        // Попадание: ближайший к земле знак с этим чтением
        hit = _drops.Select((d, i) => (d, i)).Where(x => x.d.cell.Romaji == t || (Alt.TryGetValue(x.d.cell.Romaji, out var a2) && a2.Contains(t))).OrderByDescending(x => x.d.y).First().i;
        var drop = _drops[hit];
        _drops.RemoveAt(hit);
        _combo++;
        _score += 10 * Math.Min(5, 1 + _combo / 5);
        drop.tb.Foreground = U.B("Good");
        var st = new ScaleTransform(1, 1); drop.tb.RenderTransform = st; drop.tb.RenderTransformOrigin = RelativePoint.Center;
        var tb = drop.tb;
        Anim.Opacity(tb, 1, 0, 260, 0, () => _sky.Children.Remove(tb));
        Anim.Run((st, "sc"), 1, 1.8, 260, v => { st.ScaleX = v; st.ScaleY = v; });
        ProgressStore.KanaAnswer((_kata ? "k:" : "h:") + drop.cell.Get(_kata), true);
        _input.Text = "";
        Hud();
    }

    void GameOver()
    {
        StopLoop();
        _input.IsEnabled = false;
        Children.Add(new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Children = { GameUi.Over("Игра окончена", _score, Key, Start) } }.Also(g => { SetRowSpan(g, 3); }));
    }
}

/// <summary>«Найди пару»: 12 карточек, японское слово ↔ перевод. Переворот с анимацией, озвучка при совпадении.</summary>
public sealed class MemoryGame : Grid, IPage
{
    public string Title => "Найди пару";
    readonly UniformGrid _grid = new() { Columns = 4, MaxWidth = 860 };
    readonly TextBlock _hud = U.T("", "H3", 15);
    Border? _open;
    int _moves, _pairs;
    bool _busy;
    long _start;

    public MemoryGame()
    {
        Margin = new Thickness(40, 8, 40, 20);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        Children.Add(_hud);
        Children.Add(new ScrollViewer { Content = _grid, Margin = new Thickness(0, 12, 0, 0) }.RowAt(1));
        Start();
    }

    void Start()
    {
        if (Children.Count > 2) Children.RemoveAt(2);
        var pool = Db.Words.Where(w => w.Level is "N5" or "N4" && w.Ru.Length < 26).OrderBy(w => ProgressStore.Card(w.Id) == null ? 1 : 0).ThenBy(_ => U.Rng.Next()).Take(6).ToList();
        var cards = pool.SelectMany(w => new[] { (w, jp: true), (w, jp: false) }).Shuffled();
        _grid.Children.Clear();
        _moves = 0; _pairs = 0; _open = null; _start = Environment.TickCount64;
        foreach (var (w, jp) in cards) _grid.Children.Add(Card(w, jp));
        Anim.Stagger(_grid, 30);
        Hud();
    }

    void Hud() => _hud.Text = $"Ходы: {_moves}    Пары: {_pairs}/6    Рекорд: {(GameUi.Best("memory") > 0 ? GameUi.Best("memory") + " очков" : "—")}";

    Border Card(Word w, bool jp)
    {
        var face = jp ? U.Stack(U.Jp(w.Jp, w.Jp.Length > 3 ? 26 : 34).Center(), U.T(w.Romaji, "Muted", 12).Center()) : U.Stack(U.T(w.Ru, "H3", 15).Center());
        face.VerticalAlignment = VerticalAlignment.Center;
        var back = U.Jp("?", 34, U.B("Faint"), true).Center().CenterV();
        var b = new Border { Classes = { "Card" }, Height = 120, Margin = new Thickness(6), Cursor = Cursors.Hand, Child = back, Tag = (w, jp, face, back) };
        b.PointerReleased += (_, _) => Click(b);
        return b;
    }

    async void Click(Border b)
    {
        if (_busy || b == _open || b.Tag is not (Word w, bool jp, Control face, Control back) || b.Child == face) return;
        Anim.Flip(b, () => b.Child = face);
        if (jp) Speech.Say(w.Kana);
        if (_open == null) { _open = b; return; }
        _moves++;
        var (w2, _, face2, back2) = ((Word, bool, Control, Control))_open.Tag!;
        if (w2.Id == w.Id)
        {
            _pairs++;
            b.BorderBrush = _open.BorderBrush = U.B("Good");
            b.Background = _open.Background = U.B("GoodSoft");
            Sfx.Ok();
            _open = null;
            Hud();
            if (_pairs == 6)
            {
                int secs = (int)((Environment.TickCount64 - _start) / 1000);
                int score = Math.Max(10, 300 - _moves * 10 - secs);
                await Task.Delay(500);
                Children.Add(new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Children = { GameUi.Over($"{_moves} ходов · {secs} с", score, "memory", Start) } }.Also(g => SetRowSpan(g, 2)));
            }
            return;
        }
        _busy = true;
        Sfx.Bad();
        var first = _open; _open = null;
        await Task.Delay(800);
        Anim.Flip(b, () => b.Child = back);
        Anim.Flip(first, () => first.Child = back2);
        _busy = false;
        Hud();
    }
}

/// <summary>Спринт 60 секунд: верный ли перевод? ← нет / → да. Серия верных ответов умножает очки.</summary>
public sealed class SprintGame : Grid, IPage, IKeyTarget
{
    public string Title => "Спринт 60 секунд";
    readonly ProgressBar _timer = new() { Maximum = 60, Height = 6 };
    readonly TextBlock _jp = new() { FontSize = 56, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _ru = U.T("", "H2", 24);
    readonly TextBlock _hud = U.T("", "H3", 15);
    readonly Border _card;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    List<Word> _pool = new();
    Word? _w;
    bool _truth, _over;
    int _score, _streak;
    double _left;

    public SprintGame()
    {
        Margin = new Thickness(40, 8, 40, 20);
        _jp.FontFamily = U.R<FontFamily>("JpFont"); _jp.Foreground = U.B("Text");
        var sp = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(_timer);
        sp.Children.Add(_hud.Center().M(0, 14, 0, 14));
        _card = U.Card(U.Stack(_jp, _ru.Center().M(0, 14, 0, 0)), new Thickness(30));
        sp.Children.Add(_card);
        var btns = U.Cols("*", "16", "*");
        btns.Margin = new Thickness(0, 20, 0, 0);
        btns.Children.Add(U.Btn("Неверно  ←", () => Answer(false), "", "").Also(b => b.Padding = new Thickness(16, 16, 16, 16)));
        btns.Children.Add(U.Btn("Верно  →", () => Answer(true), "Primary", "").Also(b => b.Padding = new Thickness(16, 16, 16, 16)).Col(2));
        sp.Children.Add(btns);
        Children.Add(sp);
        _tick.Tick += (_, _) => { _left -= 0.1; _timer.Value = Math.Max(0, _left); if (_left <= 0) End(); };
        Unloaded += (_, _) => _tick.Stop();
        Start();
    }

    void Start()
    {
        if (Children.Count > 1) Children.RemoveAt(1);
        _pool = Db.Words.Where(w => w.Level is "N5" or "N4" && w.Ru.Length < 30).ToList();
        _score = 0; _streak = 0; _left = 60; _over = false;
        _tick.Start();
        Next();
    }

    void Next()
    {
        _w = _pool[U.Rng.Next(_pool.Count)];
        _truth = U.Rng.Next(2) == 0;
        var shown = _truth ? _w.Ru : _pool[U.Rng.Next(_pool.Count)].Ru;
        if (shown == _w.Ru) _truth = true;
        _jp.Text = _w.Jp; _ru.Text = shown;
        _hud.Text = $"Очки: {_score}    Серия: {_streak} (×{Mult})    Рекорд: {GameUi.Best("sprint")}";
        Anim.Pop(_card, 0.94);
    }

    int Mult => 1 + Math.Min(3, _streak / 4);

    void Answer(bool yes)
    {
        if (_over || _w == null) return;
        bool ok = yes == _truth;
        if (ok) { _streak++; _score += 10 * Mult; Sfx.Ok(); }
        else { _streak = 0; Sfx.Bad(); Anim.Shake(_card); Mistakes.Add(_w.Id); }
        _card.BorderBrush = U.B(ok ? "Good" : "Bad");
        Next();
    }

    void End()
    {
        if (_over) return;
        _over = true; _tick.Stop();
        Children.Add(new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Children = { GameUi.Over("Время вышло", _score, "sprint", Start) } });
    }

    public bool HandleKey(Key key)
    {
        if (key is Key.Left or Key.D1) { Answer(false); return true; }
        if (key is Key.Right or Key.D2) { Answer(true); return true; }
        return false;
    }
}
