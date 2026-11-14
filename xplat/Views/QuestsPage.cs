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

/// <summary>
/// «Приключения»: истории из жизни в Японии. Собеседник говорит — ты выбираешь, что ответить по-японски
/// (и по желанию говоришь ответ вслух). Ошибка не обрывает историю: объяснение и вторая попытка.
/// Звёзды — за ответы с первого раза.
/// </summary>
public sealed class QuestsPage : UserControl, IPage
{
    public string Title => "Приключения";

    public static string StarsKey(Quest q) => "quest:" + q.Id;
    public static int Stars(Quest q) => ProgressStore.P.GameBest.GetValueOrDefault(StarsKey(q));

    public void OnShown()
    {
        var root = new StackPanel { MaxWidth = 1080 };
        int stars = Db.Quests.Sum(Stars), max = Db.Quests.Length * 3;
        root.Children.Add(U.Header("旅", "Приключения", $"Ты в Японии: собеседник говорит — ты выбираешь ответ по-японски и можешь сказать его вслух. Звёзды — за ответы с первого раза · собрано {stars} из {max} ★"));

        var grid = new UniformGrid { Columns = U.Columns(2), Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var q in Db.Quests) grid.Children.Add(Card(q));
        root.Children.Add(grid);

        root.Children.Add(U.Card(U.Stack(
            U.T("Как играть", "H3", 15),
            U.T("1 · Послушай реплику (кнопка 🔊 повторит, «Медленно» — по слогам).\n2 · Выбери ответ мышью или клавишами 1–3. Перевод вариантов — по кнопке «Подсказка».\n3 · После верного ответа нажми на микрофон и скажи фразу сам — это +XP и тренировка произношения.\n4 · Enter или «Дальше» — следующая сцена.", "Body", 13.5).Also(t => t.LineHeight = 22).M(0, 6, 0, 0)),
            new Thickness(20, 16, 20, 16)).M(0, 14, 0, 0));
        Anim.Stagger(grid, 40, 40);
        Content = U.Page(root);
    }

    static Control Card(Quest q)
    {
        int s = Stars(q);
        var g = U.Cols("auto", "*");
        g.Children.Add(Emoji.Img(q.Emoji, 64).Also(i => i.VerticalAlignment = VerticalAlignment.Top));
        var info = U.Stack(
            U.Row(8, U.T(q.Title, "H3", 16).CenterV(), U.Pill(q.Level).CenterV()),
            U.T(q.Desc, "Muted", 13).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MinHeight = 36; }).M(0, 4, 0, 8),
            U.Row(10,
                U.T(new string('★', s) + new string('☆', 3 - s), "H3", 17, U.B(s > 0 ? "Gold" : "Faint")).CenterV(),
                U.T($"{q.Scenes.Length} сцен", "Muted", 12.5).CenterV()));
        g.Children.Add(info.M(16, 0, 0, 0).Col(1));
        var card = new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6), Cursor = Cursors.Hand, Padding = new Thickness(18, 16) };
        HomePage.Hover(card);
        card.OnClick(() => Shell.OpenFrom(card, new QuestRun(q)));
        return card;
    }
}

/// <summary>Прохождение одного приключения.</summary>
public sealed class QuestRun : UserControl, IPage, IKeyTarget
{
    readonly Quest _q;
    int _i, _firstTry, _spoken;
    bool _missed, _solved, _hint;
    public string Title => _q.Title;

    readonly StackPanel _root = new() { MaxWidth = 820 };
    readonly ProgressBar _bar = new() { Height = 6, Minimum = 0 };
    readonly StackPanel _options = new();
    readonly ContentControl _feedback = new();
    readonly ContentControl _speak = new();
    readonly Button _next;
    readonly MicButton _mic = new();
    readonly TextBlock _micStatus = U.T("", "Muted", 13);
    Recorder? _rec;
    CancellationTokenSource? _cts;
    bool _busy;

    public QuestRun(Quest q)
    {
        _q = q;
        _bar.Maximum = q.Scenes.Length;
        _next = U.Btn("Дальше", Next, "Primary", "");
        _mic.Click += Record;
        DetachedFromVisualTree += (_, _) => { _cts?.Cancel(); _rec?.Stop(); };
        Show();
    }

    QuestScene S => _q.Scenes[_i];

    void Show()
    {
        _missed = _solved = false;
        _root.Children.Clear();
        _bar.Value = _i;
        _root.Children.Add(U.Cols("*", "auto").Also(g =>
        {
            g.Children.Add(_bar.CenterV());
            g.Children.Add(U.T($"{_i + 1} / {_q.Scenes.Length}", "Muted", 12.5).CenterV().M(12, 0, 0, 0).Col(1));
        }).M(0, 0, 0, 18));

        var sc = S;
        if (sc.Place != null) _root.Children.Add(U.T("📍 " + sc.Place.ToUpperInvariant(), "Overline").M(0, 0, 0, 6));
        if (sc.Narr != null) _root.Children.Add(U.T(sc.Narr, "Body", 15, U.B("Muted")).Also(t => { t.FontStyle = FontStyle.Italic; t.TextWrapping = TextWrapping.Wrap; }).M(0, 0, 0, 14));

        // Реплика собеседника
        var ru = U.T(sc.Ru, "Body", 14, U.B("Muted")).M(0, 6, 0, 0);
        ru.IsVisible = false;
        var bubble = U.Stack(
            U.T(sc.Who, "Overline").M(0, 0, 0, 6),
            U.Jp(sc.Jp, 26),
            ProgressStore.P.ShowRomaji ? U.T(sc.Romaji, "Muted", 13.5).M(0, 4, 0, 0) : new Control(),
            ru,
            U.Row(6,
                U.Btn("Ещё раз", () => Say(sc.Jp, sc.Male), "", ""),
                U.Btn("Медленно", () => Speech.Say(sc.Jp, true), "", ""),
                U.Btn("Перевод", () => ru.IsVisible = !ru.IsVisible, "", "")).M(0, 12, 0, 0));
        var card = U.Card(bubble, new Thickness(22, 18, 22, 18));
        card.BorderBrush = U.B("AccentSoft");
        _root.Children.Add(card);

        // Варианты ответа
        var head = U.Cols("*", "auto");
        head.Children.Add(U.T("Твой ответ", "H3", 15).CenterV());
        var hint = U.Btn(_hint ? "Скрыть перевод" : "Подсказка: перевод", () => { _hint = !_hint; FillOptions(); }, "", "");
        head.Children.Add(hint.Col(1));
        _root.Children.Add(head.M(0, 22, 0, 10));
        _root.Children.Add(_options);
        FillOptions();
        _feedback.Content = null;
        _speak.Content = null;
        _root.Children.Add(_feedback);
        _root.Children.Add(_speak);
        Anim.Stagger(_root, 40, 0, 12);
        Content = U.Page(new Grid { Children = { _root } }.Also(g => _root.HorizontalAlignment = HorizontalAlignment.Center));
        if (!ProgressStore.P.QuietMode) Say(sc.Jp, sc.Male);
    }

    // порядок вариантов стабилен в пределах сцены (иначе «Подсказка» перемешивала бы их)
    QuestChoice[] Order => S.Choices.OrderBy(c => (c.Jp.GetHashCode() ^ (_i * 7919)) & 0xffff).ToArray();
    readonly HashSet<string> _wrong = new();

    void FillOptions()
    {
        _options.Children.Clear();
        int n = 0;
        foreach (var c in Order)
        {
            n++;
            var choice = c;
            bool wrong = _wrong.Contains(c.Jp), right = _solved && c.Ok;
            var g = U.Cols("auto", "*");
            g.Children.Add(new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
                Background = U.B(right ? "GoodSoft" : wrong ? "BadSoft" : "Surface2"),
                Child = U.T(n.ToString(), "H3", 13, U.B(right ? "Good" : wrong ? "Bad" : "Muted")).Center().CenterV(),
            });
            var text = U.Stack(U.Jp(c.Jp, 19, wrong ? U.B("Faint") : null));
            if (_hint || wrong || right) text.Children.Add(U.T(c.Ru, "Muted", 13).M(0, 3, 0, 0));
            g.Children.Add(text.M(12, 0, 0, 0).Col(1));
            var b = new Border
            {
                Classes = { "Card" }, Padding = new Thickness(14, 12), Margin = new Thickness(0, 0, 0, 8), Child = g,
                Cursor = _solved || wrong ? Cursors.Arrow : Cursors.Hand,
            };
            if (right) b.BorderBrush = U.B("Good");
            else if (wrong) b.BorderBrush = U.B("Bad");
            if (!_solved && !wrong)
            {
                HomePage.Hover(b);
                b.OnClick(() => Choose(choice));
            }
            _options.Children.Add(b);
        }
    }

    void Choose(QuestChoice c)
    {
        if (_solved || _wrong.Contains(c.Jp)) return;
        if (!c.Ok)
        {
            _missed = true;
            _wrong.Add(c.Jp);
            Sfx.Bad();
            Challenges.Bump("wrong");
            FillOptions();
            ShowFeedback(false, c.Why);
            return;
        }
        _solved = true;
        if (!_missed) _firstTry++;
        Sfx.Ok();
        Challenges.Bump("correct");
        FillOptions();
        ShowFeedback(true, c.Why);
        Say(c.Jp, !S.Male);

        // Сказать вслух — по желанию
        if (Asr.Ready && Recorder.HasMicrophone && !ProgressStore.P.QuietMode)
        {
            _micStatus.Text = "Скажи свой ответ вслух — нажми на микрофон";
            var row = U.Cols("auto", "*");
            row.Children.Add(_mic.Detach());
            row.Children.Add(_micStatus.Detach().Also(t => t.TextWrapping = TextWrapping.Wrap).CenterV().M(14, 0, 0, 0).Col(1));
            _speak.Content = U.Card(row, new Thickness(16, 10, 16, 10)).M(0, 10, 0, 0);
        }
        var nb = _next.Detach();
        nb.Content = _i == _q.Scenes.Length - 1 ? "Завершить" : "Дальше";
        nb.HorizontalAlignment = HorizontalAlignment.Left; nb.Margin = new Thickness(0, 14, 0, 0);
        _root.Children.Add(nb);
        Anim.FadeUp(nb, 120, 8, 260);
    }

    void ShowFeedback(bool ok, string why)
    {
        var sp = U.Stack(U.T(ok ? "Верно!" : "Не совсем…", "H3", 15, U.B(ok ? "Good" : "Bad")), U.T(why, "Body", 14).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 4, 0, 0));
        if (!ok) sp.Children.Add(U.T("Попробуй другой вариант.", "Muted", 12.5).M(0, 4, 0, 0));
        var card = U.Card(sp, new Thickness(16, 12, 16, 12));
        card.BorderBrush = U.B(ok ? "Good" : "Bad");
        _feedback.Content = card.M(0, 4, 0, 0);
        Anim.FadeUp(card, 0, 8, 240);
    }

    static void Say(string jp, bool male) { _ = Speech.SayAsync(jp, false, male); }

    async void Record()
    {
        if (_busy) { _rec?.Stop(); return; }
        var target = S.Choices.First(c => c.Ok);
        _busy = true;
        _cts = new CancellationTokenSource();
        _rec = new Recorder();
        _rec.Started += () => Dispatcher.UIThread.Post(() => { if (_busy) _micStatus.Text = "Говори: " + target.Jp; });
        _mic.Set(MicButton.Mode.Recording, _rec);
        _micStatus.Text = "Включаю микрофон…";
        try
        {
            var kana = (Db.KanaFor(target.Jp) ?? target.Jp).Replace(" ", "");
            var (check, _) = await SpeechUi.RecordAndScore(target.Jp, kana, _rec, _cts.Token, s => { _mic.Set(MicButton.Mode.Thinking); _micStatus.Text = s; });
            if (check.Heard.Length > 0) ProgressStore.SpeechResult("q:" + target.Jp, check.Score);
            if (check.Ok)
            {
                _spoken++;
                Challenges.Bump("speak");
                ProgressStore.AddXp(2);
                Sfx.Ok();
                _micStatus.Text = $"{check.Score} — {check.Verdict}. Отлично сказано!";
            }
            else _micStatus.Text = $"{check.Score} — {check.Verdict}. Нажми ещё раз, чтобы повторить.";
        }
        catch (OperationCanceledException) { _micStatus.Text = ""; }
        catch (Exception ex) { _micStatus.Text = "Ошибка: " + ex.Message; }
        finally { _busy = false; _mic.Set(MicButton.Mode.Idle); _rec?.Dispose(); _rec = null; }
    }

    void Next()
    {
        if (!_solved) return;
        _cts?.Cancel();
        _wrong.Clear();
        if (++_i < _q.Scenes.Length) { Show(); return; }
        Finish();
    }

    void Finish()
    {
        int n = _q.Scenes.Length;
        int stars = _firstTry == n ? 3 : _firstTry >= n - 2 ? 2 : 1;
        var key = QuestsPage.StarsKey(_q);
        int best = ProgressStore.P.GameBest.GetValueOrDefault(key);
        bool first = !ProgressStore.P.Flags.Contains(key);
        if (stars > best) ProgressStore.P.GameBest[key] = stars;
        ProgressStore.P.Flags.Add(key);
        ProgressStore.AddXp(first ? 20 + stars * 5 : stars * 3);
        Sfx.Done();

        _root.Children.Clear();
        _bar.Value = n;
        var art = Art.Mascot(150, stars == 3 ? Mood.Joy : stars == 2 ? Mood.Happy : Mood.Think).Center();
        var st = U.T(new string('★', stars) + new string('☆', 3 - stars), "H1", 54, U.B("Gold")).Center();
        _root.Children.Add(U.Stack(
            art,
            U.T("Приключение пройдено!", "H1").Center().M(0, 10, 0, 4),
            U.T(_q.Title, "Muted", 15).Center(),
            st.M(0, 12, 0, 6),
            U.T($"С первого раза: {_firstTry} из {n}" + (_spoken > 0 ? $" · вслух: {_spoken}" : ""), "Body", 15).Center(),
            U.T(stars == 3 ? "Безупречно — ты справился бы и в настоящей Японии!" : stars == 2 ? "Почти идеально. Пройди ещё раз ради третьей звезды." : "Неплохо для начала: перечитай объяснения и попробуй снова.", "Muted", 13.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; }).Center().M(0, 6, 0, 18),
            U.Row(10,
                U.Btn("Пройти ещё раз", () => { _i = _firstTry = _spoken = 0; _wrong.Clear(); Show(); }, "", ""),
                U.Btn("К приключениям", Shell.Back, "Primary", "")).Center()).M(0, 20, 0, 0));
        Anim.Pop(st, 0.6);
        Content = U.Page(new Grid { Children = { _root } }.Also(g => _root.HorizontalAlignment = HorizontalAlignment.Center));
    }

    public bool HandleKey(Key key)
    {
        if (key is Key.Enter or Key.Space && _solved && _i < _q.Scenes.Length) { Next(); return true; }
        int idx = key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, _ => -1 };
        if (idx >= 0 && _i < _q.Scenes.Length && !_solved && idx < S.Choices.Length) { Choose(Order[idx]); return true; }
        return false;
    }
}
