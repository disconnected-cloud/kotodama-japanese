using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>
/// Набор на японском: печатаешь латиницей, как в японской раскладке (IME), текст сразу становится каной.
/// Подсветка верного/ошибки по знакам, скорость (знаков каны в минуту) и точность, рекорд.
/// </summary>
public sealed class TypingPage : Grid, IPage, IKeyTarget
{
    public string Title => "Набор на японском";
    static string _mode = "hira";
    static bool _hint = true;

    sealed record Item(string Target, string Show, string Ru);

    List<Item> _items = new();
    int _i, _done, _typos, _typedChars;
    long _start;
    string _lastKana = "";
    readonly TextBox _input = new() { FontSize = Shell.Compact ? 18 : 22, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center, Watermark = Shell.Compact ? "латиницей: ka, shi, kko…" : "печатай латиницей: ka, shi, kko, n…" };
    readonly TextBlock _target = new() { FontSize = 40, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _show = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    readonly TextBlock _ru = U.T("", "Muted", 14).Center();
    readonly TextBlock _romaji = U.T("", "Muted", 13).Center();
    readonly TextBlock _stats = U.T("", "H3", 14);
    readonly ProgressBar _bar = new() { Height = 6 };
    readonly Border _card = new() { Classes = { "Card" } };
    const int Count = 15;

    public TypingPage()
    {
        Margin = Shell.Compact ? new Thickness(12, 8, 12, 20) : new Thickness(40, 8, 40, 30);
        MaxWidth = 980;
        RowDefinitions = new RowDefinitions("auto,auto,auto,*");
        _target.FontFamily = U.R<FontFamily>("JpFont");
        _show.FontFamily = U.R<FontFamily>("JpFont"); _show.Foreground = U.B("Muted");
        _input.TextChanged += (_, _) => OnType();

        var modes = new WrapPanel();
        foreach (var (key, name) in new[] { ("hira", "Слова"), ("kata", "Катакана"), ("phrase", "Фразы"), ("mine", "Мои слова") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "tmode", IsChecked = _mode == key };
            rb.Checked += (_, _) => { _mode = key; Restart(); };
            modes.Children.Add(rb);
        }
        Children.Add(U.Header("打", "Набор на японском", "Печатай латиницей — текст станет каной, как в японской раскладке. Двойная согласная — っ, «nn» — ん, дефис — ー."));

        Children.Add(modes.RowAt(1).M(0, 0, 0, 12));
        var top = U.Cols("*", "auto");
        top.Children.Add(_bar.CenterV().M(0, 0, 16, 0));
        top.Children.Add(U.Row(14, _stats.CenterV(), U.Switch("Подсказка ромадзи", _hint, v => { _hint = v; _romaji.IsVisible = v; }).Also(s => s.Width = 220)).Col(1));
        Children.Add(top.RowAt(2).M(0, 0, 0, 14));

        _card.Padding = Shell.Compact ? new Thickness(16, 24) : new Thickness(30, 34);
        _input.AnswerBox(); // телефон: без «Ka» с заглавной и без подсказок клавиатуры
        _card.Child = U.Stack(_target, _show.M(0, 10, 0, 0), _ru.M(0, 4, 0, 0), _romaji.M(0, 4, 0, 18), _input,
            Shell.Compact ? U.Btn("Пропустить слово", () => { if (_input.IsEnabled) Finish(false); }, "Ghost").Center().M(0, 10, 0, 0)
                          : U.T("Enter — пропустить слово", "Muted", 12).Center().M(0, 12, 0, 0));
        Children.Add(new StackPanel { Children = { _card } }.RowAt(3));
        Restart();
    }

    static string KanaOnly(string s) => new(s.Where(c => Kana.IsKana(c) && c != '・').ToArray());

    List<Item> Pool()
    {
        var p = ProgressStore.P;
        IEnumerable<Item> src = _mode switch
        {
            "kata" => Db.Words.Where(w => w.Jp.All(c => c is >= 'ァ' and <= 'ヶ' or 'ー') && w.Jp.Length >= 2).Select(w => new Item(w.Jp, "", w.Ru)),
            "phrase" => Db.SpeechLessons.SelectMany(l => l.Items).Select(x => new Item(KanaOnly(x.KanaPlain), x.Jp, x.Ru))
                .Concat(Db.Dialogs.Where(d => d.Level is "N5" or "N4").SelectMany(d => d.Lines).Select(l => new Item(KanaOnly(l.KanaPlain), l.Jp, l.Ru)))
                .Where(i => i.Target.Length is >= 5 and <= 22),
            "mine" => Db.Words.Where(w => ProgressStore.Card(w.Id) is { Reps: > 0 }).Select(w => new Item(KanaOnly(w.Kana), w.HasKanji ? w.Jp : "", w.Ru)),
            _ => Db.Words.Where(w => w.Level is "N5" or "N4" or "1000" && KanaOnly(w.Kana).Length is >= 2 and <= 7 && !w.Jp.All(c => c is >= 'ァ' and <= 'ヶ' or 'ー'))
                .Select(w => new Item(KanaOnly(w.Kana), w.HasKanji ? w.Jp : "", w.Ru)),
        };
        var list = src.Where(i => i.Target.Length > 0).DistinctBy(i => i.Target).Shuffled().Take(Count).ToList();
        if (list.Count < 5 && _mode == "mine")
        {
            Shell.Toast("Мало изученных слов — пока тренируемся на словах N5", "打");
            _mode = "hira";
            return Pool();
        }
        return list;
    }

    void Restart()
    {
        _items = Pool();
        _i = _done = _typos = _typedChars = 0;
        _start = 0;
        if (_card.Child is not StackPanel) return;
        _bar.Maximum = _items.Count;
        Load();
    }

    Item Cur => _items[_i];

    void Load()
    {
        _bar.Value = _i;
        _lastKana = "";
        _input.Text = "";
        _input.IsEnabled = true;
        _show.Text = Cur.Show; _show.IsVisible = Cur.Show.Length > 0 && Cur.Show != Cur.Target;
        _ru.Text = Cur.Ru;
        _romaji.Text = Kana.ToRomaji(Kana.ToHiragana(Cur.Target)); _romaji.IsVisible = _hint;
        Render(0, -1);
        Anim.FadeUp(_card, 0, 8, 220);
        UpdateStats();
        Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Ввод → кана. Последняя «n» и неполный слог ещё не превращены — они «в работе».</summary>
    public static (string kana, string pending) Convert(string raw)
    {
        raw = raw.ToLowerInvariant();
        var conv = Kana.FromRomaji(raw);
        int tail = 0;
        while (tail < conv.Length && conv[^(tail + 1)] is >= 'a' and <= 'z') tail++;
        var kana = conv[..^tail];
        // одиночная «n» в конце может стать な/に… — пока не превращаем в ん
        if (tail == 0 && raw.EndsWith('n') && !raw.EndsWith("nn") && kana.EndsWith('ん')) return (kana[..^1], "n");
        return (kana, conv[^tail..]);
    }

    /// <summary>
    /// Как в японской раскладке Windows «nn» — всегда ん (kinnen → きんえん), а по Хэпбёрну konnichiwa — こんにちわ.
    /// Пробуем оба прочтения и берём то, что дальше совпадает с образцом.
    /// </summary>
    public static (string kana, string pending) Best(string raw, string target)
    {
        var a = Convert(raw);
        var b = Convert(raw.ToLowerInvariant().Replace("nn", "n'"));
        int Match((string kana, string pending) x) { int i = 0; while (i < x.kana.Length && i < target.Length && x.kana[i] == target[i]) i++; return i * 2 - (x.kana.Length - i); }
        return Match(b) > Match(a) ? b : a;
    }

    void OnType()
    {
        if (_items.Count == 0 || !_input.IsEnabled) return;
        if (_start == 0) _start = Environment.TickCount64;
        var target = Kana.ToHiragana(Cur.Target);
        var (kana, pending) = Best(_input.Text ?? "", target);
        int ok = 0;
        while (ok < kana.Length && ok < target.Length && kana[ok] == target[ok]) ok++;
        int bad = kana.Length > ok ? ok : -1;
        // новая ошибка — считаем один раз на знак
        if (bad >= 0 && kana != _lastKana && kana.Length > _lastKana.Length) { _typos++; Sfx.Bad(); }
        _lastKana = kana;
        Render(ok, bad, pending);
        if (kana == target && pending.Length == 0 || pending == "n" && kana + "ん" == target) Finish(true);
        UpdateStats();
    }

    void Render(int ok, int bad, string pending = "")
    {
        var t = Cur.Target;
        _target.Inlines!.Clear();
        _target.Inlines.Add(new Run(t[..ok]) { Foreground = U.B("Good") });
        if (bad >= 0 && bad < t.Length)
        {
            _target.Inlines.Add(new Run(t[bad].ToString()) { Foreground = U.B("Bad"), TextDecorations = TextDecorations.Underline });
            _target.Inlines.Add(new Run(t[(bad + 1)..]) { Foreground = U.B("Faint") });
        }
        else if (ok < t.Length)
        {
            _target.Inlines.Add(new Run(t[ok].ToString()) { Foreground = U.B("Text"), TextDecorations = TextDecorations.Underline });
            _target.Inlines.Add(new Run(t[(ok + 1)..]) { Foreground = U.B("Faint") });
        }
        if (pending.Length > 0) _target.Inlines.Add(new Run("  " + pending) { Foreground = U.B("Accent"), FontSize = 22 });
        _target.FontSize = (t.Length > 14 ? 30 : 40) * ProgressStore.P.JpScale;
    }

    void Finish(bool typed)
    {
        _input.IsEnabled = false;
        if (typed) { _done++; _typedChars += Cur.Target.Length; Sfx.Ok(); ProgressStore.AddXp(1); Challenges.Bump("correct"); Anim.Pop(_card, 0.97); }
        Speech.Say(Cur.Show.Length > 0 ? Cur.Show : Cur.Target);
        DispatcherTimer.RunOnce(Next, TimeSpan.FromMilliseconds(typed ? 650 : 900));
    }

    void Next()
    {
        if (++_i >= _items.Count) { Over(); return; }
        Load();
    }

    int Cpm()
    {
        if (_start == 0) return 0;
        double min = Math.Max(0.1, (Environment.TickCount64 - _start) / 60000.0);
        return (int)(_typedChars / min); // пропущенные (Enter) слова скорость не завышают
    }

    int Accuracy()
    {
        int chars = Math.Max(1, _items.Take(_i + 1).Sum(x => x.Target.Length));
        return Math.Clamp(100 - _typos * 100 / chars, 0, 100);
    }

    void UpdateStats() => _stats.Text = $"{Math.Min(_i + 1, _items.Count)} / {_items.Count}    ·    {Cpm()} зн/мин    ·    точность {Accuracy()}%";

    void Over()
    {
        int cpm = Cpm(), acc = Accuracy();
        int score = cpm * acc / 100;
        var over = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), Children = { GameUi.Over($"{cpm} зн/мин · точность {acc}%", score, "typing-" + _mode, () => { Children.RemoveAt(Children.Count - 1); Restart(); }) } };
        SetRowSpan(over, 4);
        Children.Add(over);
    }

#if DEBUG
    /// <summary>Для снимков: набрать первую половину текущего слова.</summary>
    public void DebugTypeHalf() { var r = Kana.ToRomaji(Kana.ToHiragana(Cur.Target)); _input.Text = r[..Math.Max(1, r.Length / 2 + 1)]; }
#endif

    public bool HandleKey(Key key)
    {
        if (key == Key.Enter && _input.IsEnabled) { Finish(false); return true; }
        return false;
    }
}
