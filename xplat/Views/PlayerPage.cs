using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Движок аудиоплеера «Слушай без экрана»: живёт отдельно от страницы, поэтому воспроизведение продолжается
/// при переходе в другие разделы (внизу окна — мини-плеер). Японский — пауза — перевод — японский ещё раз.
/// </summary>
public static class Player
{
    public sealed record Track(string Jp, string Kana, string Ru);

    public static string Source = "learned";
    public static bool RuFirst, RuVoice = true, Twice = true;
    public static int Pause = 3;

    public static List<Track> List { get; private set; } = new();
    public static int Index { get; private set; }
    public static bool Playing { get; private set; }
    /// <summary>Плеер запускали (есть что показать в мини-плеере), пока его не закрыли крестиком.</summary>
    public static bool Active { get; private set; }
    public static string Phase { get; private set; } = "";
    public static bool ShowJp { get; private set; } = true;
    public static bool ShowRu { get; private set; } = true;
    public static Track? Current => List.Count > 0 ? List[Math.Clamp(Index, 0, List.Count - 1)] : null;
    public static event Action? Changed;

    static int _run;   // поколение цикла: новое — старый цикл сам завершается

    static void Notify() => Changed?.Invoke();

    static string Short(string ru)
    {
        var s = ru.Split(';')[0];
        int p = s.IndexOf('(');
        if (p > 3) s = s[..p];
        return s.Trim().TrimEnd(',');
    }

    public static void Load()
    {
        Stop();
        var p = ProgressStore.P;
        IEnumerable<Track> words(IEnumerable<Word> w) => w.Select(x => new Track(x.Jp, x.HasKanji ? x.Kana : "", Short(x.Ru)));
        var list = Source switch
        {
            "due" => words(Db.Words.Where(w => ProgressStore.IsDue(w.Id))),
            "new" => words(Db.Words.Where(w => w.Level == p.Level && ProgressStore.Card(w.Id) == null).Take(30)),
            "phrases" => Db.SpeechLessons.SelectMany(l => l.Items).Select(x => new Track(x.Jp, x.Jp == x.KanaPlain ? "" : x.KanaPlain, x.Ru)),
            "dialogs" => Db.Dialogs.SelectMany(d => d.Lines).Select(l => new Track(l.Jp, l.KanaPlain == l.Jp ? "" : l.KanaPlain, l.Ru)),
            "mistakes" => words(Db.Words.Where(w => p.Mistakes.ContainsKey(w.Id))),
            _ => words(Db.Words.Where(w => ProgressStore.Card(w.Id) is { Reps: > 0 })),
        };
        // фразы диалогов идут по порядку (сюжет), остальное — вперемешку
        var l = Source == "dialogs" ? list.Take(80).ToList() : list.Shuffled().Take(60).ToList();
        if (l.Count == 0) l = words(Db.WordsOf(p.Level).Take(30)).ToList();
        List = l;
        Index = 0;
        Notify();
    }

    public static void Toggle()
    {
        if (List.Count == 0) Load();
        if (List.Count == 0) return;
        if (Playing) Stop(); else { Playing = true; Active = true; StartLoop(); Platform.Hooks.BackgroundAudio?.Invoke(true); }
        Notify();
    }

    public static void Stop()
    {
        if (Playing) Platform.Hooks.BackgroundAudio?.Invoke(false);
        Playing = false;
        _run++;
        Speech.Stop();
        Phase = "";
        ShowJp = ShowRu = true; // в режиме «сначала перевод» японский мог остаться скрытым
        Notify();
    }

    /// <summary>Закрыть мини-плеер: остановить и спрятать.</summary>
    public static void Close() { Stop(); Active = false; Notify(); }

    public static void Jump(int d)
    {
        if (List.Count == 0) return;
        Index = (Index + d + List.Count) % List.Count;
        if (Playing) StartLoop(); else { Notify(); Speech.Say(List[Index].Jp); }
    }

    public static void Select(int i)
    {
        if (i < 0 || i >= List.Count) return;
        Index = i;
        if (Playing) StartLoop(); else Notify();
    }

    static async void StartLoop()
    {
        int my = ++_run;
        Speech.Stop();
        bool Alive() => my == _run && Playing;
        async Task<bool> Wait(double sec)
        {
            for (int ms = 0; ms < sec * 1000; ms += 100) { await Task.Delay(100); if (!Alive()) return false; }
            return true;
        }
        void Set(string phase, bool jp, bool ru) { Phase = phase; ShowJp = jp; ShowRu = ru; Notify(); }
        try
        {
            while (Alive())
            {
                var t = List[Index];
                if (RuFirst)
                {
                    Set("Как это по-японски?", false, true);
                    if (RuVoice) await Speech.SayRuAsync(t.Ru);
                    if (!Alive() || !await Wait(Pause)) return;
                    Set("Слушай", true, true);
                    await Speech.SayAsync(t.Jp);
                    if (!Alive()) return;
                }
                else
                {
                    Set("Слушай и вспоминай перевод", true, false);
                    await Speech.SayAsync(t.Jp);
                    if (!Alive() || !await Wait(Pause)) return;
                    Set("Перевод", true, true);
                    if (RuVoice) await Speech.SayRuAsync(t.Ru); else if (!await Wait(1.2)) return;
                    if (!Alive()) return;
                }
                if (Twice)
                {
                    if (!await Wait(0.6)) return;
                    Set("Ещё раз", true, true);
                    await Speech.SayAsync(t.Jp);
                    if (!Alive()) return;
                }
                if (!await Wait(1.2)) return;
                Challenges.Bump("listen");
                Index = (Index + 1) % List.Count;
                Notify();
            }
        }
        catch (Exception e) { CrashReport.Warn("Аудиоплеер", e); }
    }
}

/// <summary>Страница аудиоплеера: источник, настройки, текущая карточка и очередь. Пробел — пауза, ←/→ — листать.</summary>
public sealed class PlayerPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Аудиоплеер";

    readonly TextBlock _jp = new() { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _kana = U.T("", "Muted", 16).Center();
    readonly TextBlock _ru = U.T("", "H2", 20).Center();
    readonly TextBlock _pos = U.T("", "Muted", 13).Center();
    readonly TextBlock _phase = U.T("", "Muted", 12).Center();
    readonly ProgressBar _bar = new() { Height = 4 };
    readonly Button _playBtn = new();
    readonly StackPanel _queue = new();
    readonly ScrollViewer _queueScroll = new();
    List<Player.Track>? _shownList;

    public PlayerPage()
    {
        _jp.FontFamily = U.R<FontFamily>("JpFont");
        // подписка только пока страница на экране; уход со страницы воспроизведение не останавливает — дальше играет мини-плеер
        AttachedToVisualTree += (_, _) => { Player.Changed += Render; Render(); };
        DetachedFromVisualTree += (_, _) => Player.Changed -= Render;
        if (Player.List.Count == 0) Player.Load();
        Build();
    }

    void Build()
    {
        var root = new StackPanel { MaxWidth = 1080 };
        root.Children.Add(U.Header("播", "Аудиоплеер", "Слушай без экрана: японский, пауза, чтобы вспомнить, перевод и ещё раз японский. " + (Shell.Compact ? "Можно выключить экран — плеер продолжит играть, остановить можно из уведомления." : "Можно уйти в другой раздел — плеер продолжит внизу окна.")));

        var src = new WrapPanel();
        foreach (var (key, name) in new[] { ("learned", "Изученные слова"), ("due", "На повторение"), ("new", "Новые слова уровня"), ("mistakes", "Мои ошибки"), ("phrases", "Разговорник"), ("dialogs", "Диалоги") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "psrc", IsChecked = Player.Source == key, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => { if (Player.Source == key) return; Player.Source = key; Player.Load(); };
            src.Children.Add(rb);
        }
        root.Children.Add(src);

        Panel opts = Shell.Compact ? new StackPanel { Margin = new Thickness(0, 6, 0, 14) } : new WrapPanel { Margin = new Thickness(0, 6, 0, 14) };
        Control Sw(string text, bool on, Action<bool> set, double w) => U.Switch(text, on, set).Also(s => { if (!Shell.Compact) s.Width = w; else s.HorizontalAlignment = HorizontalAlignment.Stretch; }).M(0, 0, 24, 0);
        opts.Children.Add(Sw("Сначала перевод (вспомни по-японски)", Player.RuFirst, v => Player.RuFirst = v, 340));
        opts.Children.Add(Sw("Перевод голосом", Player.RuVoice, v => Player.RuVoice = v, 220));
        opts.Children.Add(Sw("Японский дважды", Player.Twice, v => Player.Twice = v, 210));
        var pauses = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pauses.Children.Add(U.T("Пауза:", "Muted").CenterV().M(0, 0, 8, 0));
        foreach (var sec in new[] { 1, 3, 5 })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = sec + " с", GroupName = "ppause", IsChecked = Player.Pause == sec };
            rb.Checked += (_, _) => Player.Pause = sec;
            pauses.Children.Add(rb);
        }
        opts.Children.Add(pauses);
        root.Children.Add(opts);

        _playBtn.Classes.Add("Primary");
        _playBtn.Width = 64; _playBtn.Height = 64; _playBtn.CornerRadius = new CornerRadius(32);
        _playBtn.HorizontalContentAlignment = HorizontalAlignment.Center; _playBtn.VerticalContentAlignment = VerticalAlignment.Center;
        _playBtn.Click += (_, _) => Player.Toggle();
        var controls = U.Row(18, MiniPlayer.Round("prev", "Назад (←)", () => Player.Jump(-1), 46).CenterV(), _playBtn, MiniPlayer.Round("next", "Вперёд (→)", () => Player.Jump(1), 46).CenterV()).Center();
        var stage = U.Stack(_pos, _jp.M(0, 18, 0, 0), _kana.M(0, 6, 0, 0), _ru.M(0, 14, 0, 0), _phase.M(0, 12, 0, 18), _bar.M(0, 0, 0, 20), controls,
            U.T("Пробел — пауза · ← → — листать", "Muted", 12).Center().M(0, 12, 0, 0).DesktopOnly());
        var stageCard = U.Card(stage, new Thickness(30, 26));

        _queueScroll.Content = _queue;
        _queueScroll.MaxHeight = 420;
        var side = U.Card(U.Stack(U.T("Очередь", "H3", 15).M(0, 0, 0, 8), _queueScroll), new Thickness(16));
        var g = U.Stackify(U.Cols("*", "20", "320").Also(c =>
        {
            c.Children.Add(stageCard);
            c.Children.Add(side.Col(2));
        }));
        root.Children.Add(g);
        Content = U.Page(root);
    }

    void BuildQueue()
    {
        _shownList = Player.List;
        _queue.Children.Clear();
        for (int k = 0; k < Player.List.Count; k++)
        {
            int idx = k;
            var t = Player.List[k];
            var row = new Border { Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var rg = U.Cols("*", "auto");
            rg.Children.Add(U.Jp(t.Jp, 15).Also(j => { j.TextWrapping = TextWrapping.NoWrap; j.TextTrimming = TextTrimming.CharacterEllipsis; }));
            rg.Children.Add(U.T(t.Ru, "Muted", 12).Also(x => { x.MaxWidth = 140; x.TextTrimming = TextTrimming.CharacterEllipsis; x.TextWrapping = TextWrapping.NoWrap; }).CenterV().M(10, 0, 0, 0).Col(1));
            row.Child = rg;
            row.OnClick(() => Player.Select(idx));
            _queue.Children.Add(row);
        }
    }

    int _lastIndex = -1;
    void Render()
    {
        if (!ReferenceEquals(_shownList, Player.List)) { BuildQueue(); _lastIndex = -1; }
        var t = Player.Current;
        if (t == null) return;
        _jp.Text = t.Jp; _jp.FontSize = (t.Jp.Length > 14 ? 34 : 54) * ProgressStore.P.JpScale;
        _kana.Text = t.Kana; _kana.IsVisible = t.Kana.Length > 0;
        _ru.Text = t.Ru;
        double jo = Player.ShowJp ? 1 : 0, ro = Player.ShowRu ? 1 : 0;
        if (Math.Abs(_jp.Opacity - jo) > 0.01) Anim.Opacity(_jp, _jp.Opacity, jo, 200);
        _kana.Opacity = jo;
        if (Math.Abs(_ru.Opacity - ro) > 0.01) Anim.Opacity(_ru, _ru.Opacity, ro, 200);
        _phase.Text = Player.Phase;
        _pos.Text = $"{Player.Index + 1} из {Player.List.Count}";
        _bar.Maximum = Player.List.Count; _bar.Value = Player.Index + 1;
        _playBtn.Content = Art.Glyph(Player.Playing ? "pause" : "play", 26, U.B("OnAccent"));
        if (_lastIndex != Player.Index)
        {
            for (int k = 0; k < _queue.Children.Count; k++)
                if (_queue.Children[k] is Border b) b.Background = k == Player.Index ? U.B("AccentSoft") : Brushes.Transparent;
            if (_queue.Children.Count > Player.Index) _queue.Children[Player.Index].BringIntoView();
            _lastIndex = Player.Index;
        }
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space: Player.Toggle(); return true;
            case Key.Left: Player.Jump(-1); return true;
            case Key.Right: Player.Jump(1); return true;
        }
        return false;
    }
}

/// <summary>Мини-плеер внизу окна: виден, пока плеер запущен, а открыт другой раздел.</summary>
public sealed class MiniPlayer : Border
{
    readonly TextBlock _jp = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
    readonly TextBlock _ru = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
    readonly Button _play;
    public bool OnPlayerPage;

    public static Button Round(string glyph, string tip, Action a, double size = 34)
    {
        var b = new Button { Content = Art.Glyph(glyph, size * 0.38, U.B("Text")), Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center }.Tip(tip);
        b.Click += (_, _) => a();
        return b;
    }

    public MiniPlayer()
    {
        _jp.FontFamily = U.R<FontFamily>("JpFont");
        _ru.Foreground = U.B("Muted");
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 18); Padding = new Thickness(10, 8, 10, 8);
        CornerRadius = new CornerRadius(26); Background = U.B("Surface3"); BorderBrush = U.B("StrokeStrong"); BorderThickness = new Thickness(1);
        if (!Shell.Compact) BoxShadow = BoxShadows.Parse("0 8 28 0 #66000000");
        IsVisible = false;
        _play = Round("play", "Пауза / продолжить", Player.Toggle, 38);
        _play.Classes.Add("Primary");
        var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 14, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        info.Children.Add(Art.Icon("headphones", 26).CenterV());
        info.Children.Add(_jp.M(10, 0, 10, 0));
        info.Children.Add(_ru);
        info.OnClick(() => Shell.Open(new PlayerPage()));
        ToolTip.SetTip(info, "Открыть аудиоплеер");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Round("prev", "Назад", () => Player.Jump(-1)).CenterV());
        row.Children.Add(_play.M(6, 0, 6, 0));
        row.Children.Add(Round("next", "Вперёд", () => Player.Jump(1)).CenterV());
        row.Children.Add(info.CenterV());
        row.Children.Add(Round("close", "Закрыть плеер", Player.Close, 30).CenterV());
        Child = row;
        Player.Changed += Render;
    }

    public void Render()
    {
        bool show = Player.Active && !OnPlayerPage && !Shell.Compact; // телефон: прежнее поведение, без мини-плеера
        if (show != IsVisible)
        {
            IsVisible = show;
            if (show) Anim.FadeUp(this, 0, 14, 260);
        }
        if (!show || Player.Current is not { } t) return;
        _jp.Text = Player.ShowJp ? t.Jp : "・・・";
        _ru.Text = Player.ShowRu ? t.Ru : Player.Phase;
        _play.Content = Art.Glyph(Player.Playing ? "pause" : "play", 15, U.B("OnAccent"));
    }
}
