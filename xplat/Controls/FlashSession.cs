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
using Matsubakira.Services;

namespace Matsubakira.Controls;

public sealed class StudyCard
{
    public string Id = "";
    public string Front = "";
    public double FrontSize = 96;
    public string? Hint;
    public string Reading = "";
    public string Romaji = "";
    public string Meaning = "";
    public string? Extra;            // доп. информация (чтения кандзи и т.п.)
    public Example[] Examples = Array.Empty<Example>();
    public string Speak = "";
    public bool IsNew;
    public string? Art;              // иллюстрация темы
}

/// <summary>Сессия флеш-карточек с интервальным повторением: «показать ответ» → оценка 1–4.</summary>
public sealed class FlashSession : Grid, IPage, IKeyTarget
{
    public string Title { get; }
    readonly List<StudyCard> _queue;
    readonly int _total;
    int _idx, _done, _again, _xp;
    bool _flipped;
    StudyCard C => _queue[_idx];

    readonly Grid _main = new();
    readonly ProgressBar _bar = new() { Height = 10, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _count = new() { FontSize = 13, Foreground = U.B("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    readonly Border _card;
    readonly ContentControl _face = new();
    readonly Grid _bottom = new() { MaxWidth = 760, Height = 92 };

    public FlashSession(string title, IEnumerable<StudyCard> cards)
    {
        Title = title;
        _queue = cards.ToList();
        _total = _queue.Count;
        Margin = Shell.Compact ? new Thickness(12, 8, 12, 12) : new Thickness(40, 8, 40, 24);

        _main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _main.RowDefinitions.Add(new RowDefinition());
        _main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_main);

        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(_bar);
        SetColumn(_count, 1); top.Children.Add(_count);
        _main.Children.Add(top);

        _card = new Border
        {
            Classes = { "Card" }, MaxWidth = 760, MinHeight = 380, Padding = new Thickness(36),
            Margin = new Thickness(0, 10, 0, 22), Cursor = Cursors.Hand, Child = _face,
            BoxShadow = BoxShadows.Parse("0 6 24 0 #59000000"),
        };
        _card.PointerReleased += (_, _) => { if (!_flipped) Flip(); };
        SetRow(_card, 1); _main.Children.Add(_card);
        SetRow(_bottom, 2); _main.Children.Add(_bottom);

        if (_total == 0) { ShowEmpty(); return; }
        Render();
    }

    public bool IsEmpty => _total == 0;

    void ShowEmpty()
    {
        Children.Clear();
        Children.Add(U.Stack(
            Art.Mascot(200, Mood.Joy).Center().M(0, 0, 0, 10),
            U.Jp("お疲れ様！", 44, U.B("Accent"), true).Center(),
            U.T("Сейчас нечего повторять — всё выучено вовремя.", "H2").Center().M(0, 12, 0, 6),
            U.T("Загляни позже или выучи новые слова.", "Muted").Center(),
            U.Row(10, U.Btn("Учить новые слова", () => Shell.Go("words"), "Primary"), U.Btn("Сыграть в игру", () => Shell.Go("trainers"), "")).Center().M(0, 24, 0, 0)
        ).CenterV());
    }

    void Render()
    {
        _flipped = false;
        _bar.Maximum = _total; _bar.Value = _done;
        _count.Text = $"{Math.Min(_done + 1, _total)} / {_total}" + (_again > 0 ? $"  ·  повторов {_again}" : "");
        _face.Content = Front(C);
        Anim.FadeUp(_card, 0, 18, 320);

        _bottom.Children.Clear();
        _bottom.ColumnDefinitions.Clear();
        var show = U.Btn(Shell.Compact ? "Показать ответ" : "Показать ответ · пробел", Flip, "Primary");
        show.MinWidth = 280; show.HorizontalAlignment = HorizontalAlignment.Center; show.VerticalAlignment = VerticalAlignment.Top; show.Padding = new Thickness(24, 14, 24, 14);
        _bottom.Children.Add(show);
    }

    Control Front(StudyCard c)
    {
        var g = new Grid();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (c.Art != null) sp.Children.Add(Emoji.Img(c.Art, 52).Center().M(0, 0, 0, 10));
        sp.Children.Add(U.Jp(c.Front, c.FrontSize).Center());
        if (c.Hint != null) sp.Children.Add(U.T(c.Hint, "Muted", 14).Center().M(0, 14, 0, 0));
        g.Children.Add(sp);
        if (c.IsNew)
        {
            var pill = U.Pill("НОВОЕ", U.B("AccentGradient"), Brushes.White);
            pill.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(pill);
        }
        var hint = U.T(Shell.Compact ? "Нажми на карточку" : "Нажми на карточку или пробел", "Muted", 12);
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Bottom; hint.Foreground = U.B("Faint");
        g.Children.Add(hint);
        return g;
    }

    Control Back(StudyCard c)
    {
        var g = new Grid();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(U.Jp(c.Front, Math.Min(c.FrontSize, 64)).Center());
        if (c.Reading.Length > 0 && c.Reading != c.Front) sp.Children.Add(U.Jp(c.Reading, 22, U.B("Accent")).Center().M(0, 6, 0, 0));
        if (ProgressStore.P.ShowRomaji && c.Romaji.Length > 0) sp.Children.Add(U.T(c.Romaji, "Muted", 14).Center().M(0, 2, 0, 0));
        if (!c.Id.StartsWith("k:") && Pitch.Pattern(c.Front, c.Reading) is { } pat)
            sp.Children.Add(new AccentView(pat) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0)}.Tip(Pitch.Describe(Pitch.Accent(c.Front, c.Reading) ?? 0, pat.Count - 1)));
        sp.Children.Add(new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(60, 18, 60, 16) });
        sp.Children.Add(U.T(c.Meaning, "H2", 24).Center());
        if (c.Extra != null) sp.Children.Add(U.T(c.Extra, "Muted", 13).Center().M(0, 8, 0, 0));
        // своя заметка к слову (страница слова в словаре)
        if (ProgressStore.P.Notes.TryGetValue(c.Id, out var note))
            sp.Children.Add(new Border
            {
                Background = U.B("AccentSoft"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520,
                Child = U.T("✎ " + note, "Body", 13).Also(t => t.TextWrapping = TextWrapping.Wrap),
            });
        foreach (var ex in c.Examples.Take(2))
        {
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var line = U.Row(8, U.Jp(ex.Jp, 17), U.SpeakBtnSmall(ex.Jp));
            line.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(line);
            row.Children.Add(U.T($"{ex.Romaji} — {ex.Ru}", "Muted", 12.5).Center());
            sp.Children.Add(row);
        }
        g.Children.Add(sp);
        var bs = U.Row(6, U.SpeakBtn(() => c.Speak, true), U.SpeakBtn(() => c.Speak));
        bs.HorizontalAlignment = HorizontalAlignment.Right; bs.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(bs);
        return g;
    }

    void Flip()
    {
        if (_flipped || _idx >= _queue.Count) return;
        _flipped = true;
        var c = C;
        Anim.Flip(_card, () => _face.Content = Back(c));
        if (ProgressStore.AutoSpeakOn) Speech.Say(c.Speak);

        _bottom.Children.Clear();
        var card = ProgressStore.Card(c.Id);
        (Rating r, string label, string brush)[] defs =
        {
            (Rating.Again, "Снова", "Bad"), (Rating.Hard, "Трудно", "Gold"),
            (Rating.Good, "Хорошо", "Good"), (Rating.Easy, "Легко", "Info"),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            _bottom.ColumnDefinitions.Add(new ColumnDefinition());
            var (r, label, brush) = defs[i];
            var content = U.Stack(
                U.T(label, "H3", 15, U.B(brush)).Center(),
                U.T($"{i + 1} · {ProgressStore.FormatInterval(ProgressStore.Preview(card, r))}", "Muted", 12).Center().M(0, 3, 0, 0));
            var b = new Button { Content = content, Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(10, 12, 10, 12), VerticalAlignment = VerticalAlignment.Top };
            b.Click += (_, _) => Rate(r);
            SetColumn(b, i);
            _bottom.Children.Add(b);
        }
        Anim.Stagger(_bottom, 30, 120);
    }

    void Rate(Rating r)
    {
        if (!_flipped) return;
        var c = C;
        ProgressStore.Rate(c.Id, r);
        if (r == Rating.Again) Mistakes.Add(c.Id); else Mistakes.Correct(c.Id);
        _xp += r == Rating.Again ? 1 : 3;
        if (r == Rating.Again)
        {
            _again++;
            c.IsNew = false;
            _queue.Insert(Math.Min(_queue.Count, _idx + 4), c); // вернётся через пару карточек
        }
        else _done++;
        _idx++;
        if (_idx >= _queue.Count) { Finish(); return; }
        Render();
    }

    void Finish()
    {
        int pct = _total == 0 ? 100 : (int)Math.Round(100.0 * _total / (_total + _again));
        Children.Clear();
        Children.Add(Results.Build(pct, _xp, new[]
        {
            ("Карточек", _total.ToString()),
            ("С первого раза", $"{_total - Math.Min(_total, _again)}"),
            ("Следующее повторение", "по расписанию SRS"),
        }, null, Shell.Back, this));
    }

    public bool HandleKey(Key key)
    {
        if (Children.Count == 0 || Children[0] != _main) return false;
        if (key is Key.Space or Key.Enter)
        {
            if (!_flipped) { Flip(); return true; }
            if (key == Key.Enter || key == Key.Space) { Rate(Rating.Good); return true; }
        }
        if (_flipped)
        {
            int n = key switch { Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2, Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4, _ => 0 };
            if (n > 0) { Rate((Rating)n); return true; }
        }
        if (key == Key.R && _idx < _queue.Count) { Speech.Say(C.Speak); return true; }
        return false;
    }

    // ─────────── Фабрики карточек ───────────
    public static StudyCard FromWord(Word w) => new()
    {
        Id = w.Id, Front = w.Jp, FrontSize = w.Jp.Length > 5 ? 64 : 92, Hint = w.Topic,
        Reading = w.Kana, Romaji = w.Romaji, Meaning = w.Ru, Speak = w.Kana,
        Examples = w.ExJp.Length > 0 ? new[] { new Example(w.ExJp, "", w.ExRu) } : Array.Empty<Example>(),
        IsNew = ProgressStore.Card(w.Id) == null,
        Art = Emoji.ForTopic(w.Topic),
    };

    public static StudyCard FromKanji(KanjiInfo k) => new()
    {
        Id = k.Id, Front = k.Char, FrontSize = 130, Hint = "Кандзи · " + k.Strokes + " черт",
        Reading = k.Kun.Length > 0 ? k.Kun : k.On, Romaji = "", Meaning = k.Ru,
        Extra = $"он: {k.On}    кун: {(k.Kun.Length > 0 ? k.Kun : "—")}" + (Mnemonics.Hint(k.Char, k.Ru) is { Length: > 0 } h ? "\n💡 " + h : ""),
        Examples = k.Examples, Speak = k.Examples.Length > 0 ? k.Examples[0].Kana : k.Char,
        IsNew = ProgressStore.Card(k.Id) == null,
    };
}
