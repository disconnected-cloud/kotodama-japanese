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

/// <summary>
/// Минимальные пары: слова, которые отличаются одним звуком (долгота, っ, ゃ/や, звонкость).
/// Сначала тренируется слух (викторина), затем произношение: Whisper получает в подсказке ОБА слова пары,
/// поэтому не «подтягивает» ответ к нужному, а честно выбирает, на какое из двух похоже сказанное.
/// </summary>
public sealed class PairsPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Минимальные пары";

    sealed record Word(string Kana, string Kanji, string Ru)
    {
        public string Shown => Kanji.Length > 0 && Kanji != Kana ? Kanji : Kana;
    }
    sealed record Pair(string Group, Word A, Word B);

    static Pair P(string g, string a, string ak, string ar, string b, string bk, string br) => new(g, new(a, ak, ar), new(b, bk, br));

    static readonly Pair[] All =
    {
        P("long", "おばさん", "叔母さん", "тётя", "おばあさん", "お婆さん", "бабушка"),
        P("long", "おじさん", "叔父さん", "дядя", "おじいさん", "お爺さん", "дедушка"),
        P("long", "ゆき", "雪", "снег", "ゆうき", "勇気", "смелость"),
        P("long", "いえ", "家", "дом", "いいえ", "", "нет"),
        P("long", "ここ", "", "здесь", "こうこう", "高校", "старшая школа"),
        P("long", "しゅじん", "主人", "муж, хозяин", "しゅうじん", "囚人", "заключённый"),
        P("long", "へや", "部屋", "комната", "へいや", "平野", "равнина"),
        P("long", "とる", "取る", "брать", "とおる", "通る", "проходить"),
        P("long", "さと", "里", "деревня", "さとう", "砂糖", "сахар"),
        P("long", "ちず", "地図", "карта", "ちいず", "チーズ", "сыр"),
        P("tsu", "きて", "来て", "приди", "きって", "切手", "почтовая марка"),
        P("tsu", "さか", "坂", "склон", "さっか", "作家", "писатель"),
        P("tsu", "おと", "音", "звук", "おっと", "夫", "муж"),
        P("tsu", "かこ", "過去", "прошлое", "かっこ", "格好", "внешний вид"),
        P("tsu", "いた", "居た", "был (о живом)", "いった", "行った", "сходил"),
        P("tsu", "した", "下", "низ", "しった", "知った", "узнал"),
        P("ya", "びよういん", "美容院", "салон красоты", "びょういん", "病院", "больница"),
        P("ya", "じゆう", "自由", "свобода", "じゅう", "十", "десять"),
        P("ya", "りよう", "利用", "использование", "りょう", "寮", "общежитие"),
        P("ya", "きやく", "規約", "правила, устав", "きゃく", "客", "гость"),
        P("ya", "ひやく", "飛躍", "скачок", "ひゃく", "百", "сто"),
        P("voice", "かき", "柿", "хурма", "かぎ", "鍵", "ключ"),
        P("voice", "てんき", "天気", "погода", "でんき", "電気", "электричество"),
        P("voice", "たいがく", "退学", "отчисление", "だいがく", "大学", "университет"),
        P("voice", "くち", "口", "рот", "ぐち", "愚痴", "жалоба, нытьё"),
    };

    static readonly (string key, string title, string tip)[] Groups =
    {
        ("all", "Все", "Смесь всех типов."),
        ("long", "Долгие гласные", "Долгая гласная тянется вдвое дольше обычной. От неё зависит смысл: おばさん — тётя, おばあさん — бабушка."),
        ("tsu", "Пауза っ", "Маленькая っ — короткая пауза (на один слог) перед согласным: «ки-[пауза]-тэ»."),
        ("ya", "や или ゃ", "Большая や — отдельный слог «и-я», маленькая ゃ сливается с предыдущим в один: びよう (би-ё-у) ≠ びょう (бё-у)."),
        ("voice", "Звонкие и глухие", "か/が, た/だ, く/ぐ: различаются только голосом на согласном."),
    };

    string _group = "all";
    Pair _p = All[0];
    bool _targetA;
    int _ok, _total;
    Recorder? _rec;
    CancellationTokenSource? _cts;
    bool _busy;
    Recording? _last;

    readonly StackPanel _root = new() { MaxWidth = 880 };
    readonly ContentControl _card = new();
    readonly TextBlock _status = new() { FontSize = 13, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    readonly ContentControl _result = new();
    readonly SpeechUi.LevelMeter _meter = new();
    readonly MicButton _mic = new();
    readonly TextBlock _scoreText = U.T("", "Muted", 12.5);

    public PairsPage()
    {
        _status.Foreground = U.B("Muted");
        _mic.Click += Record;
        Unloaded += (_, _) => { _cts?.Cancel(); _rec?.Stop(); };
        Asr.Warmup();
        Build();
        NextPair();
    }

    void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(U.Header("対", "Минимальные пары", "Слова, которые отличаются одним звуком. Сначала научись слышать разницу, потом — произносить её."));

        var chips = new WrapPanel();
        foreach (var (key, title, _) in Groups)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = title, GroupName = "pairs", IsChecked = _group == key, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => { _group = key; Build(); NextPair(); };
            chips.Children.Add(rb);
        }
        _root.Children.Add(chips);
        var tip = Groups.First(g => g.key == _group).tip;
        _root.Children.Add(new Border
        {
            Background = U.B("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 4, 0, 16),
            Child = U.Row(10, new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Center }, U.T(tip, "Body", 13).Also(t => t.MaxWidth = 760)),
        });

        var listen = U.Btn("Тренировка слуха: 12 вопросов", ListenQuiz, "Primary", "");
        listen.HorizontalAlignment = HorizontalAlignment.Left;
        _root.Children.Add(listen);

        _root.Children.Add(U.T("Произношение", "H2").M(0, 26, 0, 4));
        _root.Children.Add(U.T("Скажи выделенное слово. Приложение решит, на какое из двух оно больше похоже.", "Muted").M(0, 0, 0, 12));
        _root.Children.Add(_card);

        var mic = new Grid { Width = 140, Height = 116, HorizontalAlignment = HorizontalAlignment.Center };
        _root.Children.Add(_mic.M(0, 4, 0, -12));
        _root.Children.Add(_meter);
        _root.Children.Add(_status);
        _root.Children.Add(_result.M(0, 12, 0, 0));

        var nav = U.Cols("auto", "*", "auto");
        nav.Margin = new Thickness(0, 18, 0, 0);
        nav.Children.Add(_scoreText.Detach().CenterV());
        nav.Children.Add(U.T("Пробел — говорить · L — послушать · → — другая пара", "Muted", 12).DesktopOnly().Center().CenterV().Col(1));
        nav.Children.Add(U.Btn("Другая пара", NextPair, "", "").Col(2));
        _root.Children.Add(nav);
        Content = U.Page(_root);
    }

    IEnumerable<Pair> Pool => _group == "all" ? All : All.Where(p => p.Group == _group);

    void NextPair()
    {
        _cts?.Cancel();
        var pool = Pool.ToList();
        if (pool.Count > 1) pool.Remove(_p);
        _p = pool[Random.Shared.Next(pool.Count)];
        _targetA = Random.Shared.Next(2) == 0;
        _result.Content = null;
        _last = null;
        _status.Text = Asr.Ready ? "Послушай оба слова, затем нажми на микрофон и скажи выделенное" : "Для проверки произношения скачай модель Whisper в разделе «Говорение». Слушать можно уже сейчас.";
        RenderCard();
        Speech.Say((_targetA ? _p.A : _p.B).Kana);
    }

    void RenderCard()
    {
        var g = U.Cols("*", "16", "*");
        g.Children.Add(WordTile(_p.A, _targetA));
        g.Children.Add(WordTile(_p.B, !_targetA).Col(2));
        _card.Content = g;
        Anim.FadeUp(g, 0, 10, 280);
        _scoreText.Text = _total > 0 ? $"Верно {_ok} из {_total}" : "";
    }

    static Border WordTile(Word w, bool target)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(U.T(target ? "СКАЖИ" : "НЕ ПУТАЙ С", "Overline").Center().Also(t => t.Foreground = U.B(target ? "Accent" : "Muted")));
        sp.Children.Add(U.Jp(w.Kana, 38, null, true).Center().M(0, 6, 0, 0));
        if (w.Shown != w.Kana) sp.Children.Add(U.Jp(w.Shown, 15, U.B("Muted")).Center().M(0, 2, 0, 0));
        sp.Children.Add(U.T(Kana.ToRomaji(w.Kana), "Body", 14, U.B("Muted")).Center().M(0, 2, 0, 0));
        sp.Children.Add(U.T(w.Ru, "H3", 15).Center().M(0, 8, 0, 0));
        sp.Children.Add(U.Row(4, U.SpeakBtn(() => w.Kana, true), U.SpeakBtn(() => w.Kana)).Center().M(0, 10, 0, 0));
        var card = U.Card(sp, new Thickness(18, 16, 18, 16));
        card.BorderBrush = U.B(target ? "Accent" : "Stroke");
        card.BorderThickness = new Thickness(target ? 2 : 1);
        card.Opacity = target ? 1 : 0.8;
        return card;
    }

    async void Record()
    {
        if (_busy) { _rec?.Stop(); return; }
        if (!Asr.Ready) { Shell.Toast("Сначала скачай модель распознавания (раздел «Говорение»)", "!"); return; }
        if (!Recorder.HasMicrophone) { _status.Text = "Микрофон не найден."; return; }
        var pair = _p; bool targetA = _targetA;
        var target = targetA ? pair.A : pair.B;
        _busy = true;
        Speech.Stop();
        _cts = new CancellationTokenSource();
        _rec = new Recorder();
        _rec.Started += () => Dispatcher.UIThread.Post(() => { if (_busy) _status.Text = $"Говори: {target.Kana}"; });
        SetMic(true);
        _status.Text = "Включаю микрофон…";
        _result.Content = null;
        try
        {
            var r = await _rec.RecordAsync(_cts.Token);
            _last = r;
            SetMic(false);
            if (r.Seconds < 0.2 || (!r.Heard && r.PeakLevel < 0.02f)) { _status.Text = "Голос не услышан — говори громче или проверь микрофон в разделе «Говорение»."; return; }
            _status.Text = "Распознаю…"; _mic.Set(MicButton.Mode.Thinking); _meter.Attach(null);
            await JDict.LoadAsync();
            // В подсказке оба слова: модель знает словарь пары, но не знает, какое из них «правильное»
            var (text, _) = await Asr.TranscribeAsync(r.Wav, _cts.Token, $"{pair.A.Shown}、{pair.B.Shown}");
            text = Speech.DropHallucination(text, "");
            var map = new Dictionary<string, string>();
            foreach (var w in new[] { pair.A, pair.B })
                foreach (var kv in Reading.Align(w.Shown, w.Kana)) map.TryAdd(kv.Key, kv.Value);
            var heard = Kana.ToHiragana(Reading.Of(text, map));
            ShowResult(pair, targetA, text, heard, r);
        }
        catch (OperationCanceledException) { _status.Text = ""; }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
        finally { _busy = false; SetMic(false); _rec?.Dispose(); _rec = null; }
    }

    void ShowResult(Pair pair, bool targetA, string raw, string heard, Recording r)
    {
        _status.Text = "";
        var target = targetA ? pair.A : pair.B;
        var other = targetA ? pair.B : pair.A;
        string ph = Reading.Phon(heard);
        double st = Reading.SoundSimilarity(ph, Reading.Phon(target.Kana));
        double so = Reading.SoundSimilarity(ph, Reading.Phon(other.Kana));
        bool both = heard.Contains(Kana.ToHiragana(pair.A.Kana)) && heard.Contains(Kana.ToHiragana(pair.B.Kana)) && pair.A.Kana != pair.B.Kana;
        string verdict; string brush; bool ok = false;
        if (ph.Length == 0 || both || Math.Max(st, so) < 0.45)
        { verdict = "Не удалось разобрать слово — скажи его отчётливо, отдельно от других."; brush = "Gold"; }
        else if (st > so + 0.001)
        { ok = true; verdict = $"Верно! Прозвучало «{target.Kana}» — {target.Ru}."; brush = "Good"; }
        else if (Math.Abs(st - so) <= 0.001)
        { verdict = "Звучит как что-то среднее между двумя словами. Преувеличь разницу и повтори."; brush = "Gold"; }
        else
        { verdict = $"Похоже на «{other.Kana}» — {other.Ru}. А нужно «{target.Kana}» — {target.Ru}."; brush = "Bad"; }

        if (ph.Length > 0 && !both)
        {
            _total++; if (ok) { _ok++; Sfx.Ok(); } else Sfx.Bad();
            ProgressStore.SpeechResult("pair:" + target.Kana, ok ? 90 : 30);
        }
        _scoreText.Text = $"Верно {_ok} из {_total}";

        var sp = new StackPanel();
        sp.Children.Add(U.T(verdict, "H3", 15, U.B(brush)));
        if (raw.Length > 0) sp.Children.Add(U.T($"Распознано: {raw.Trim()}" + (heard != raw.Trim() ? $" ({heard})" : ""), "Muted", 13).M(0, 6, 0, 0));
        if (!ok)
        {
            var tip = Groups.First(g => g.key == pair.Group).tip;
            sp.Children.Add(U.T(tip, "Body", 13).M(0, 8, 0, 0).Also(t => t.MaxWidth = 700));
        }
        var acts = U.Row(8,
            U.Btn("Ещё раз", Record, "", ""),
            U.Btn("Моя запись", () => _ = Speech.PlayWavAsync(r.Wav), "", ""),
            U.Btn("Образец медленно", () => Speech.Say(target.Kana, true), "", ""),
            U.Btn("Следующая пара", NextPair, ok ? "Primary" : "", ""));
        acts.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(acts);
        var card = U.Card(sp);
        card.BorderBrush = U.B(brush);
        _result.Content = card;
        Anim.FadeUp(card, 0, 10, 280);
    }

    void SetMic(bool on)
    {
        _mic.Set(on ? MicButton.Mode.Recording : MicButton.Mode.Idle, _rec);
        _meter.Attach(on ? _rec : null);
    }

    /// <summary>Тренировка слуха: звучит одно слово пары — выбери, какое.</summary>
    void ListenQuiz()
    {
        var qs = new List<QuizQ>();
        var pool = Pool.ToList();
        for (int i = 0; i < 12; i++)
        {
            var p = pool[Random.Shared.Next(pool.Count)];
            var w = Random.Shared.Next(2) == 0 ? p.A : p.B;
            qs.Add(new QuizQ
            {
                Prompt = "聞いて", PromptSize = 52, Sub = "Какое слово прозвучало? · 🔊 повторить",
                SpeakOnShow = true, Speak = w.Kana, Answer = w.Kana,
                Options = new[] { p.A.Kana, p.B.Kana }.Shuffled().ToArray(), OptionsJp = true,
                Explain = $"{p.A.Kana} — {p.A.Ru} · {p.B.Kana} — {p.B.Ru}",
            });
        }
        Shell.Open(new QuizSession("Минимальные пары: слух", qs));
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space: Record(); return true;
            case Key.L: Speech.Say((_targetA ? _p.A : _p.B).Kana); return true;
            case Key.M: if (_last != null) _ = Speech.PlayWavAsync(_last.Wav); return true;
            case Key.Right: NextPair(); return true;
        }
        return false;
    }
}
