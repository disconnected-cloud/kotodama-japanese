using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Слова для игр: сначала уже изучаемые (закрепление), если их мало — частые слова N5.</summary>
static class GamePool
{
    public static List<Word> Words(int min = 16, int maxLen = 6)
    {
        var learned = Db.Words.Where(w => ProgressStore.Card(w.Id) != null && w.Jp.Length <= maxLen && w.Ru.Length <= 24).ToList();
        if (learned.Count >= min) return learned.Shuffled();
        var basic = Db.Words.Where(w => w.Level == "N5" && w.Jp.Length <= maxLen && w.Ru.Length <= 24).Take(300);
        return learned.Concat(basic).DistinctBy(w => w.Id).Shuffled();
    }

    /// <summary>Шапка игры: заголовок, счёт и полоска времени.</summary>
    public static Grid Hud(string title, TextBlock score, ProgressBar? timer)
    {
        var g = U.Cols("auto", "*", "auto");
        g.Margin = new Thickness(0, 0, 0, 14);
        g.Children.Add(U.T(title, "H2").CenterV());
        if (timer != null) { timer.Height = 8; timer.Margin = new Thickness(24, 0, 24, 0); timer.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(timer.Col(1)); }
        g.Children.Add(score.CenterV().Col(2));
        return g;
    }
}

/// <summary>
/// «Пары на скорость»: слева японские слова, справа переводы. Соедини пары, пока идёт минута —
/// найденная пара сразу заменяется новой. Ошибка отнимает 3 секунды.
/// </summary>
public sealed class MatchRushGame : Grid, IPage
{
    public string Title => "Пары на скорость";
    const string Key = "match";
    const int Rows = 5;
    readonly TextBlock _score = U.T("", "H3", 16);
    readonly ProgressBar _timer = new() { Maximum = 60 };
    readonly Grid _board = new() { ColumnDefinitions = new ColumnDefinitions("*,24,*"), Width = 680 };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    List<Word> _pool = new();
    int _next, _points, _combo, _pairs;
    double _left;
    Button? _selJp, _selRu;
    readonly Dictionary<Button, Word> _map = new();

    public MatchRushGame()
    {
        Margin = new Thickness(40, 8, 40, 24);
        _tick.Tick += (_, _) => { _left -= 0.2; _timer.Value = Math.Max(0, _left); if (_left <= 0) Over(); };
        Unloaded += (_, _) => _tick.Stop();
        Start();
    }

    void Start()
    {
        Children.Clear();
        _pool = GamePool.Words(); _next = 0; _points = 0; _combo = 0; _pairs = 0; _left = 60;
        _map.Clear(); _selJp = _selRu = null;
        _board.Children.Clear(); _board.RowDefinitions.Clear();
        for (int r = 0; r < Rows; r++) _board.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var words = Enumerable.Range(0, Rows).Select(_ => Take()).ToList();
        var ru = words.Shuffled();
        for (int r = 0; r < Rows; r++) { Place(words[r], r, true); Place(ru[r], r, false); }
        var root = new StackPanel();
        root.Children.Add(GamePool.Hud(Title, _score, _timer));
        root.Children.Add(U.T("Нажми японское слово, затем его перевод. Ошибка — минус 3 секунды.", "Muted").Center().M(0, 0, 0, 16));
        root.Children.Add(_board.Center());
        Children.Add(root);
        Hud();
        _tick.Start();
    }

    Word Take() { if (_next >= _pool.Count) { _pool = _pool.Shuffled(); _next = 0; } return _pool[_next++]; }

    void Place(Word w, int row, bool jp)
    {
        var b = new Button
        {
            Content = jp ? (object)U.Jp(w.Jp, w.Jp.Length > 4 ? 20 : 24) : U.T(w.Ru, "Body", 15).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; }),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 70, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(12, 8),
        };
        _map[b] = w;
        b.Click += (_, _) => Pick(b, jp);
        Grid.SetRow(b, row); Grid.SetColumn(b, jp ? 0 : 2);
        _board.Children.Add(b);
        Anim.Pop(b, 0.9);
    }

    void Mark(Button? b, bool on) { if (b != null) { b.BorderBrush = on ? U.B("Accent") : U.B("Stroke"); b.Background = on ? U.B("AccentSoft") : U.B("Surface"); } }

    void Pick(Button b, bool jp)
    {
        if (_left <= 0) return;
        if (jp) { Mark(_selJp, false); _selJp = b; Speech.Say(_map[b].Kana); } else { Mark(_selRu, false); _selRu = b; }
        Mark(b, true);
        if (_selJp == null || _selRu == null) return;
        var a = _selJp; var r = _selRu;
        _selJp = _selRu = null;
        if (_map[a] == _map[r] || _map[a].Ru == _map[r].Ru)
        {
            _combo++; _pairs++;
            _points += 10 + Math.Min(_combo, 10) * 2;
            Sfx.Ok();
            foreach (var x in new[] { a, r }) { x.Background = U.B("GoodSoft"); x.BorderBrush = U.B("Good"); x.IsEnabled = false; }
            int row = Grid.GetRow(a), rowR = Grid.GetRow(r);
            DispatcherTimer.RunOnce(() =>
            {
                _board.Children.Remove(a); _board.Children.Remove(r); _map.Remove(a); _map.Remove(r);
                var w = Take();
                // новая пара: японское — на место старого, перевод — на освободившееся место справа
                Place(w, row, true); Place(w, rowR, false);
            }, TimeSpan.FromMilliseconds(260));
        }
        else
        {
            _combo = 0; _left -= 3;
            Sfx.Bad();
            Mistakes.Add(_map[a].Id);
            foreach (var x in new[] { a, r }) { x.Background = U.B("BadSoft"); x.BorderBrush = U.B("Bad"); }
            DispatcherTimer.RunOnce(() => { Mark(a, false); Mark(r, false); }, TimeSpan.FromMilliseconds(350));
        }
        Hud();
    }

    void Hud() => _score.Text = $"{_points} очков · ×{Math.Max(1, _combo)}";

    void Over()
    {
        _tick.Stop();
        Children.Clear();
        Children.Add(GameUi.Over($"Найдено пар: {_pairs}", _points, Key, Start));
    }
}

/// <summary>«Собери слово»: перевод и озвучка — собери чтение из слогов каны (есть лишние).</summary>
public sealed class WordBuilderGame : Grid, IPage, IKeyTarget
{
    public string Title => "Собери слово";
    const string Key = "build";
    const int Rounds = 10;
    readonly TextBlock _score = U.T("", "H3", 16);
    readonly ProgressBar _bar = new() { Maximum = Rounds };
    readonly ContentControl _area = new();
    List<Word> _words = new();
    int _round, _points, _mistakes;
    readonly List<Button> _picked = new();
    readonly WrapPanel _answer = new() { HorizontalAlignment = HorizontalAlignment.Center, MinHeight = 64 };
    readonly WrapPanel _tiles = new() { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 640 };
    bool _locked;

    public WordBuilderGame()
    {
        Margin = new Thickness(40, 8, 40, 24);
        Start();
    }

    void Start()
    {
        _words = GamePool.Words(Rounds, 5).Where(w => w.Kana.Length is >= 2 and <= 6).Take(Rounds).ToList();
        _round = 0; _points = 0; _mistakes = 0;
        Children.Clear();
        var root = new StackPanel();
        root.Children.Add(GamePool.Hud(Title, _score, _bar));
        root.Children.Add(_area);
        Children.Add(root);
        Next();
    }

    void Next()
    {
        if (_round >= _words.Count) { Children.Clear(); Children.Add(GameUi.Over($"Ошибок: {_mistakes}", _points, Key, Start)); return; }
        _locked = false;
        _bar.Value = _round;
        _score.Text = $"{_points} очков";
        var w = _words[_round];
        _picked.Clear(); _answer.Children.Clear(); _tiles.Children.Clear();

        // слоги: маленькие ゃゅょっ и ー — вместе с предыдущим знаком
        var syl = new List<string>();
        foreach (var ch in w.Kana)
            if (syl.Count > 0 && "ゃゅょぁぃぅぇぉャュョァィゥェォー".Contains(ch)) syl[^1] += ch; else syl.Add(ch.ToString());
        bool kata = w.Kana.Any(c => c >= 'ァ' && c <= 'ヶ');
        var extra = Kana.All.Select(c => kata ? c.Kata : c.Hira).Where(h => !syl.Contains(h)).Shuffled().Take(Math.Max(2, 6 - syl.Count));
        foreach (var s in syl.Concat(extra).Shuffled())
        {
            var b = new Button { Content = U.Jp(s, 26), MinWidth = 64, MinHeight = 64, Margin = new Thickness(5), Tag = s };
            b.Click += (_, _) => Tap(b);
            _tiles.Children.Add(b);
        }
        Anim.Stagger(_tiles, 25, 0, 8);

        var sp = new StackPanel();
        var art = Emoji.ForTopic(w.Topic);
        sp.Children.Add(Emoji.Img(art, 56).Center());
        sp.Children.Add(U.T(w.Ru, "H1", 28).Center().M(0, 10, 0, 4).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; }));
        sp.Children.Add(U.Row(6, U.SpeakBtn(() => w.Kana, true), U.SpeakBtn(() => w.Kana)).Center());
        sp.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 18), Padding = new Thickness(10), CornerRadius = new CornerRadius(16), MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Center,
            Background = U.B("Surface2"), BorderBrush = U.B("Stroke"), BorderThickness = new Thickness(1), Child = _answer,
        });
        sp.Children.Add(_tiles);
        var acts = U.Row(10, U.Btn("Стереть", Undo, "Ghost", ""), U.Btn("Подсказка (−5)", () => Hint(syl), "Ghost", ""));
        acts.HorizontalAlignment = HorizontalAlignment.Center; acts.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(acts);
        _area.Content = U.Card(sp, new Thickness(28, 22, 28, 22)).Also(c => c.MaxWidth = 760);
        Anim.FadeUp((Control)_area.Content, 0, 12, 280);
        if (ProgressStore.P.AutoSpeak) Speech.Say(w.Kana);
    }

    void Tap(Button b)
    {
        if (_locked || !b.IsEnabled) return;
        b.IsEnabled = false; b.Opacity = 0.35;
        _picked.Add(b);
        _answer.Children.Add(Chip((string)b.Tag!, "AccentSoft", "Text").Also(c => Anim.Pop(c)));
        Check();
    }

    static Border Chip(string s, string bg, string fg) => new()
    {
        Background = U.B(bg), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6), Margin = new Thickness(3), Child = U.Jp(s, 24, U.B(fg)),
    };

    void Undo()
    {
        if (_locked || _picked.Count == 0) return;
        var b = _picked[^1]; _picked.RemoveAt(_picked.Count - 1);
        b.IsEnabled = true; b.Opacity = 1;
        _answer.Children.RemoveAt(_answer.Children.Count - 1);
    }

    void Hint(List<string> syl)
    {
        if (_locked) return;
        while (_picked.Count > 0 && string.Concat(_picked.Select(p => (string)p.Tag!)) != string.Concat(syl.Take(_picked.Count))) Undo();
        if (_picked.Count >= syl.Count) return;
        var need = syl[_picked.Count];
        var b = _tiles.Children.OfType<Button>().FirstOrDefault(x => x.IsEnabled && (string)x.Tag! == need);
        _points = Math.Max(0, _points - 5);
        if (b != null) Tap(b);
    }

    void Check()
    {
        var w = _words[_round];
        var built = string.Concat(_picked.Select(p => (string)p.Tag!));
        if (built.Length < w.Kana.Length) return;
        _locked = true;
        bool ok = built == w.Kana;
        _answer.Children.Clear();
        _answer.Children.Add(Chip(w.Kana, ok ? "GoodSoft" : "BadSoft", ok ? "Good" : "Bad"));
        if (!ok) _answer.Children.Add(U.T("— правильно: " + w.Kana + (w.Jp != w.Kana ? $" ({w.Jp})" : ""), "Body", 14).CenterV().M(8, 0, 0, 0));
        else if (w.Jp != w.Kana) _answer.Children.Add(U.Jp(w.Jp, 22, U.B("Muted")).CenterV().M(10, 0, 0, 0));
        if (ok) { _points += 15; Sfx.Ok(); Mistakes.Correct(w.Id); } else { _mistakes++; Sfx.Bad(); Mistakes.Add(w.Id); }
        Speech.Say(w.Kana);
        _round++;
        DispatcherTimer.RunOnce(Next, TimeSpan.FromMilliseconds(ok ? 1100 : 2200));
    }

    public bool HandleKey(Key key)
    {
        if (key == Avalonia.Input.Key.Back) { Undo(); return true; }
        if (key == Avalonia.Input.Key.R && _round < _words.Count) { Speech.Say(_words[_round].Kana); return true; }
        return false;
    }
}

/// <summary>«Ловец звуков»: звучит слово — успей нажать на него среди шести. Чем быстрее, тем больше очков.</summary>
public sealed class ListenCatchGame : Grid, IPage, IKeyTarget
{
    public string Title => "Ловец звуков";
    const string Key = "listen";
    const int Rounds = 12;
    readonly TextBlock _score = U.T("", "H3", 16);
    readonly ProgressBar _timer = new() { Maximum = 8 };
    readonly UniformGrid _grid = new() { Columns = 3, MaxWidth = 720 };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly ContentControl _mascot = new();
    List<Word> _pool = new();
    Word? _target;
    int _round, _points, _hits;
    double _left;
    bool _locked;

    public ListenCatchGame()
    {
        Margin = new Thickness(40, 8, 40, 24);
        _tick.Tick += (_, _) =>
        {
            if (_locked) return;
            _left -= 0.1; _timer.Value = Math.Max(0, _left);
            if (_left <= 0) Answer(null);
        };
        Unloaded += (_, _) => _tick.Stop();
        Start();
    }

    void Start()
    {
        _pool = GamePool.Words(18);
        _round = 0; _points = 0; _hits = 0;
        Children.Clear();
        var root = new StackPanel();
        root.Children.Add(GamePool.Hud(Title, _score, _timer));
        var top = U.Row(14, _mascot, U.Stack(U.T("Слушай и лови слово!", "H3", 16), U.T("R — повторить звук. Чем быстрее ответ, тем больше очков.", "Muted")).CenterV());
        top.HorizontalAlignment = HorizontalAlignment.Center; top.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(top);
        root.Children.Add(_grid.Center());
        var again = U.Btn("Повторить звук", () => { if (_target != null) Speech.Say(_target.Kana); }, "", "");
        again.HorizontalAlignment = HorizontalAlignment.Center; again.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(again);
        Children.Add(root);
        _tick.Start();
        Next();
    }

    void Next()
    {
        if (_round >= Rounds) { _tick.Stop(); Children.Clear(); Children.Add(GameUi.Over($"Поймано {_hits} из {Rounds}", _points, Key, Start)); return; }
        _mascot.Content = Art.Tama(64, Mood.Think);
        var opts = _pool.Skip(_round * 3 % Math.Max(1, _pool.Count - 6)).Take(6).ToList();
        if (opts.Count < 6) opts = _pool.Shuffled().Take(6).ToList();
        _target = opts[U.Rng.Next(opts.Count)];
        _grid.Children.Clear();
        foreach (var w in opts.Shuffled())
        {
            var b = new Button
            {
                Content = U.Stack(U.Jp(w.Jp, w.Jp.Length > 3 ? 26 : 34).Center(), U.T(w.Ru, "Muted", 12).Center().Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis)),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 104, Margin = new Thickness(6), Tag = w,
            };
            b.Click += (_, _) => Answer(b);
            _grid.Children.Add(b);
        }
        Anim.Stagger(_grid, 30, 0, 10);
        _left = 8; _timer.Value = 8; _locked = false;
        _score.Text = $"{_points} очков · {_round + 1}/{Rounds}";
        Speech.Say(_target.Kana);
    }

    void Answer(Button? b)
    {
        if (_locked || _target == null) return;
        _locked = true;
        bool ok = b != null && ((Word)b.Tag!).Kana == _target.Kana;
        foreach (var x in _grid.Children.OfType<Button>())
            if (((Word)x.Tag!).Kana == _target.Kana) { x.Background = U.B("GoodSoft"); x.BorderBrush = U.B("Good"); }
        if (ok) { _hits++; _points += 10 + (int)Math.Round(_left * 2.5); Sfx.Ok(); Mistakes.Correct(_target.Id); }
        else
        {
            if (b != null) { b.Background = U.B("BadSoft"); b.BorderBrush = U.B("Bad"); }
            Sfx.Bad(); Mistakes.Add(_target.Id);
        }
        _mascot.Content = Art.Tama(64, ok ? Mood.Joy : Mood.Sad);
        _round++;
        DispatcherTimer.RunOnce(Next, TimeSpan.FromMilliseconds(ok ? 900 : 1700));
    }

    public bool HandleKey(Key key)
    {
        if (key == Avalonia.Input.Key.R && _target != null) { Speech.Say(_target.Kana); return true; }
        int n = key switch { Avalonia.Input.Key.D1 => 1, Avalonia.Input.Key.D2 => 2, Avalonia.Input.Key.D3 => 3, Avalonia.Input.Key.D4 => 4, Avalonia.Input.Key.D5 => 5, Avalonia.Input.Key.D6 => 6, _ => 0 };
        if (n > 0 && n <= _grid.Children.Count && _grid.Children[n - 1] is Button b) { Answer(b); return true; }
        return false;
    }
}
