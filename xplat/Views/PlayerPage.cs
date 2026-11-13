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
/// Аудиоплеер «Слушай без экрана»: слова и фразы по кругу — японский, пауза, перевод по-русски, японский ещё раз.
/// Можно свернуть окно и слушать, пока занят другим. Пробел — пауза, ←/→ — назад/вперёд.
/// </summary>
public sealed class PlayerPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Аудиоплеер";
    static string _src = "learned";
    static bool _ruFirst, _ruVoice = true, _twice = true;
    static int _pause = 3;

    sealed record Track(string Jp, string Kana, string Ru);

    List<Track> _list = new();
    int _i;
    bool _playing;
    int _run;  // поколение цикла воспроизведения: новое — старый цикл сам завершается
    readonly TextBlock _jp = new() { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _kana = U.T("", "Muted", 16).Center();
    readonly TextBlock _ru = U.T("", "H2", 20).Center();
    readonly TextBlock _pos = U.T("", "Muted", 13).Center();
    readonly TextBlock _phase = U.T("", "Muted", 12).Center();
    readonly ProgressBar _bar = new() { Height = 4 };
    readonly Button _playBtn = new();
    readonly StackPanel _queue = new();
    readonly ScrollViewer _queueScroll = new();

    public PlayerPage()
    {
        _jp.FontFamily = U.R<FontFamily>("JpFont");
        Unloaded += (_, _) => Stop();
        Build();
    }

    static string Short(string ru)
    {
        var s = ru.Split(';')[0];
        int p = s.IndexOf('(');
        if (p > 3) s = s[..p];
        return s.Trim().TrimEnd(',');
    }

    List<Track> Load()
    {
        var p = ProgressStore.P;
        IEnumerable<Track> words(IEnumerable<Word> w) => w.Select(x => new Track(x.Jp, x.HasKanji ? x.Kana : "", Short(x.Ru)));
        var list = _src switch
        {
            "due" => words(Db.Words.Where(w => ProgressStore.IsDue(w.Id))),
            "new" => words(Db.Words.Where(w => w.Level == p.Level && ProgressStore.Card(w.Id) == null).Take(30)),
            "phrases" => Db.SpeechLessons.SelectMany(l => l.Items).Select(x => new Track(x.Jp, x.Jp == x.KanaPlain ? "" : x.KanaPlain, x.Ru)),
            "dialogs" => Db.Dialogs.SelectMany(d => d.Lines).Select(l => new Track(l.Jp, l.KanaPlain == l.Jp ? "" : l.KanaPlain, l.Ru)),
            "mistakes" => words(Db.Words.Where(w => p.Mistakes.ContainsKey(w.Id))),
            _ => words(Db.Words.Where(w => ProgressStore.Card(w.Id) is { Reps: > 0 })),
        };
        // фразы диалогов идут по порядку (сюжет), остальное — вперемешку
        var l = _src == "dialogs" ? list.Take(80).ToList() : list.Shuffled().Take(60).ToList();
        if (l.Count == 0) l = words(Db.WordsOf(p.Level).Take(30)).ToList();
        return l;
    }

    void Build()
    {
        var root = new StackPanel { MaxWidth = 1080 };
        root.Children.Add(U.Header("播", "Аудиоплеер", "Слушай без экрана: японский, пауза, чтобы вспомнить, перевод и ещё раз японский. Окно можно свернуть."));

        var src = new WrapPanel();
        foreach (var (key, name) in new[] { ("learned", "Изученные слова"), ("due", "На повторение"), ("new", "Новые слова уровня"), ("mistakes", "Мои ошибки"), ("phrases", "Разговорник"), ("dialogs", "Диалоги") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "psrc", IsChecked = _src == key, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => { _src = key; Reload(); };
            src.Children.Add(rb);
        }
        root.Children.Add(src);

        var opts = new WrapPanel { Margin = new Thickness(0, 6, 0, 14) };
        opts.Children.Add(U.Switch("Сначала перевод (вспомни по-японски)", _ruFirst, v => _ruFirst = v).Also(s => s.Width = 340).M(0, 0, 24, 0));
        opts.Children.Add(U.Switch("Перевод голосом", _ruVoice, v => _ruVoice = v).Also(s => s.Width = 220).M(0, 0, 24, 0));
        opts.Children.Add(U.Switch("Японский дважды", _twice, v => _twice = v).Also(s => s.Width = 210).M(0, 0, 24, 0));
        var pauses = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pauses.Children.Add(U.T("Пауза:", "Muted").CenterV().M(0, 0, 8, 0));
        foreach (var sec in new[] { 1, 3, 5 })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = sec + " с", GroupName = "ppause", IsChecked = _pause == sec };
            rb.Checked += (_, _) => _pause = sec;
            pauses.Children.Add(rb);
        }
        opts.Children.Add(pauses);
        root.Children.Add(opts);

        // ── сцена: текущая карточка и кнопки
        _playBtn.Classes.Add("Primary");
        _playBtn.Width = 64; _playBtn.Height = 64; _playBtn.CornerRadius = new CornerRadius(32);
        _playBtn.HorizontalContentAlignment = HorizontalAlignment.Center; _playBtn.VerticalContentAlignment = VerticalAlignment.Center;
        _playBtn.Click += (_, _) => Toggle();
        Button Round(string glyph, string tip, Action a)
        {
            var b = new Button { Content = Art.Glyph(glyph, 18, U.B("Text")), Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center }.Tip(tip);
            b.Click += (_, _) => a();
            return b;
        }
        var controls = U.Row(18, Round("prev", "Назад (←)", () => Jump(-1)).CenterV(), _playBtn, Round("next", "Вперёд (→)", () => Jump(1)).CenterV()).Center();
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
        Reload();
    }

    void Reload()
    {
        Stop();
        _list = Load();
        _i = 0;
        _queue.Children.Clear();
        for (int k = 0; k < _list.Count; k++)
        {
            int idx = k;
            var t = _list[k];
            var row = new Border { Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = idx };
            var rg = U.Cols("*", "auto");
            rg.Children.Add(U.Jp(t.Jp, 15).Also(j => { j.TextWrapping = TextWrapping.NoWrap; j.TextTrimming = TextTrimming.CharacterEllipsis; }));
            rg.Children.Add(U.T(t.Ru, "Muted", 12).Also(x => { x.MaxWidth = 140; x.TextTrimming = TextTrimming.CharacterEllipsis; x.TextWrapping = TextWrapping.NoWrap; }).CenterV().M(10, 0, 0, 0).Col(1));
            row.Child = rg;
            row.OnClick(() => { _i = idx; Show(); if (_playing) StartLoop(); });
            _queue.Children.Add(row);
        }
        Show();
    }

    void Show()
    {
        if (_list.Count == 0) return;
        var t = _list[_i];
        _jp.Text = t.Jp; _jp.FontSize = (t.Jp.Length > 14 ? 34 : 54) * ProgressStore.P.JpScale;
        _kana.Text = t.Kana; _kana.IsVisible = t.Kana.Length > 0;
        _ru.Text = t.Ru;
        _pos.Text = $"{_i + 1} из {_list.Count}";
        _bar.Maximum = _list.Count; _bar.Value = _i + 1;
        _playBtn.Content = Art.Glyph(_playing ? "pause" : "play", 26, U.B("OnAccent"));
        for (int k = 0; k < _queue.Children.Count; k++)
            if (_queue.Children[k] is Border b) b.Background = k == _i ? U.B("AccentSoft") : Brushes.Transparent;
        if (_queue.Children.Count > _i) _queue.Children[_i].BringIntoView();
    }

    void Toggle()
    {
        if (_playing) Stop(); else { _playing = true; StartLoop(); }
        Show();
    }

    void Stop()
    {
        _playing = false;
        _run++;
        Speech.Stop();
        _phase.Text = "";
        Show();
    }

    void Jump(int d)
    {
        if (_list.Count == 0) return;
        _i = (_i + d + _list.Count) % _list.Count;
        Show();
        if (_playing) StartLoop(); else Speech.Say(_list[_i].Jp);
    }

    async void StartLoop()
    {
        int my = ++_run;
        Speech.Stop();
        bool Alive() => my == _run && _playing;
        async Task<bool> Wait(double sec)
        {
            for (int ms = 0; ms < sec * 1000; ms += 100) { await Task.Delay(100); if (!Alive()) return false; }
            return true;
        }
        try
        {
            while (Alive())
            {
                var t = _list[_i];
                Show();
                if (_ruFirst)
                {
                    _jp.Opacity = 0.0; _kana.Opacity = 0; _ru.Opacity = 1;
                    _phase.Text = "Как это по-японски?";
                    if (_ruVoice) await Speech.SayRuAsync(t.Ru);
                    if (!Alive() || !await Wait(_pause)) return;
                    Anim.Opacity(_jp, 0, 1, 200); Anim.Opacity(_kana, 0, 1, 200);
                    _phase.Text = "Слушай";
                    await Speech.SayAsync(t.Jp);
                    if (!Alive()) return;
                }
                else
                {
                    _jp.Opacity = 1; _kana.Opacity = 1; _ru.Opacity = 0;
                    _phase.Text = "Слушай и вспоминай перевод";
                    await Speech.SayAsync(t.Jp);
                    if (!Alive() || !await Wait(_pause)) return;
                    Anim.Opacity(_ru, 0, 1, 200);
                    _phase.Text = "Перевод";
                    if (_ruVoice) await Speech.SayRuAsync(t.Ru); else if (!await Wait(1.2)) return;
                    if (!Alive()) return;
                }
                if (_twice)
                {
                    if (!await Wait(0.6)) return;
                    _phase.Text = "Ещё раз";
                    await Speech.SayAsync(t.Jp);
                    if (!Alive()) return;
                }
                if (!await Wait(1.2)) return;
                Challenges.Bump("listen");
                _i = (_i + 1) % _list.Count;
            }
        }
        catch (Exception e) { CrashReport.Warn("Аудиоплеер", e); }
        finally { if (my == _run) { _jp.Opacity = _kana.Opacity = _ru.Opacity = 1; } }
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space: Toggle(); return true;
            case Key.Left: Jump(-1); return true;
            case Key.Right: Jump(1); return true;
        }
        return false;
    }
}
