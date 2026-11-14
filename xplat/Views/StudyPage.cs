using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Controls;
using Matsubakira.Services;
using ToggleButton = Matsubakira.Compat.ToggleButton;

namespace Matsubakira.Views;

/// <summary>Шаг умной сессии: что делать, почему именно это, и как запустить (false — делать нечего, шаг пропускается).</summary>
public sealed record StudyStage(string Icon, string Title, string Why, int Minutes, Func<bool> Launch);

/// <summary>«Учиться»: одна кнопка — приложение само собирает сессию по статистике
/// (повторение по FSRS, ошибки, новое по пути, аудирование, речь) и ведёт по шагам без возврата в меню.</summary>
public sealed class StudyPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Учиться";

    /// <summary>Незаконченная сессия сегодняшнего дня — кнопка «Учиться» продолжает её.</summary>
    public static StudyPage? Active;
    /// <summary>Сейчас идёт шаг сессии: итоговые экраны пишут «Дальше» вместо «Готово».</summary>
    public static bool InFlow => Active is { _launched: true };
    public bool IsResumable => !_finished && _day == DateTime.Today && _started;

    public static readonly (string name, string sub, int minutes)[] Modes =
    {
        ("Лёгкая", "~5 минут", 5), ("Обычная", "~15 минут", 15), ("Интенсив", "~30 минут", 30),
    };

    int _mode;
    List<StudyStage> _stages = new();
    readonly List<int> _state = new();   // 0 — впереди, 1 — сделано, 2 — пропущено
    int _cur = -1;
    bool _launched, _finished;
    int _xpStart;
    bool _started;
    int _secondsBefore;                  // время, проведённое в сессии до перезапуска приложения
    readonly Stopwatch _clock = new();
    int Seconds => _secondsBefore + (int)_clock.Elapsed.TotalSeconds;
    readonly DateTime _day = DateTime.Today;
    DispatcherTimer? _auto;

    StudyPage(int mode) { _mode = mode; _stages = Plan(mode); _state.AddRange(_stages.Select(_ => 0)); Prefetch(mode); }

    static Task _prefetch = Task.CompletedTask;

    /// <summary>
    /// Озвучка для ближайшей сессии — заранее, в фоне и по порядку шагов: повторение, новые слова, аудирование, говорение.
    /// Особенно важно для VOICEVOX — синтез на процессоре занимает до секунды на фразу, а из кэша звук мгновенный.
    /// </summary>
    public static void Prefetch(int mode)
    {
        if (!_prefetch.IsCompleted || Platform.AudioPlayer.Muted || !Tts.UseOffline) return; // системный голос быстр и без предзагрузки
        var p = ProgressStore.P;
        int cap = mode switch { 0 => 12, 1 => 30, _ => 60 };
        var texts = new List<string>();
        texts.AddRange(Db.Words.Where(w => ProgressStore.IsDue(w.Id)).OrderBy(w => ProgressStore.Card(w.Id)?.Due).Take(cap).Select(w => w.Kana));
        texts.AddRange(Db.Words.Where(w => w.Level == p.Level && ProgressStore.Card(w.Id) == null).Take(8).Select(w => w.Kana));
        texts.AddRange(Db.Words.Where(w => ProgressStore.Card(w.Id)?.Reps > 0).OrderBy(w => ProgressStore.Card(w.Id)!.Stability).Take(12).Select(w => w.Kana));
        var lesson = Db.SpeechLessons.OrderBy(l => l.Items.Count(it => p.SpeechBest.ContainsKey(it.Id)) / (double)Math.Max(1, l.Items.Length)).ThenBy(l => l.Id).FirstOrDefault();
        if (lesson != null) texts.AddRange(lesson.Items.Take(8).Select(it => it.Jp));
        var todo = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(50).Where(t => !Tts.IsCached(t)).ToList();
        if (todo.Count == 0) return;
        _prefetch = Task.Run(async () =>
        {
            // по одной фразе: живая озвучка (нажал «повторить») не ждёт в очереди за всей пачкой
            foreach (var t in todo)
                try { if (await Tts.GetAsync(t) == null) return; } // VOICEVOX сломался — дальше не пробуем
                catch (Exception e) { CrashReport.Warn("Предзагрузка озвучки", e); return; }
            Perf.Mark($"озвучка сессии готова ({todo.Count})");
        });
    }

    /// <summary>Открыть сессию: продолжить сегодняшнюю (в том числе после перезапуска) или собрать новую.</summary>
    public static void Start()
    {
        if (Active is { _finished: false } a && a._day == DateTime.Today) { Shell.Open(a); return; }
        Active = Restore() ?? new StudyPage(Math.Clamp(ProgressStore.P.StudyMode, 0, 2));
        Shell.Open(Active);
    }

    /// <summary>Есть ли сегодня начатая и не законченная сессия (в памяти или сохранённая).</summary>
    public static bool HasPaused => Active is { IsResumable: true } || (Active == null && ProgressStore.P.StudyDay == Today);
    static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>Сохранить ход сессии в прогресс — чтобы после перезапуска продолжить с того же шага.</summary>
    void Save()
    {
        var p = ProgressStore.P;
        p.StudyDay = Today;
        p.StudyDone = _stages.Where((_, i) => _state[i] == 1).Select(s => s.Title).ToList();
        p.StudySkipped = _stages.Where((_, i) => _state[i] == 2).Select(s => s.Title).ToList();
        p.StudySeconds = Seconds;
        p.StudyXpStart = _xpStart;
        ProgressStore.Notify();
    }

    static StudyPage? Restore()
    {
        var p = ProgressStore.P;
        if (p.StudyDay != Today || p.Flags.Contains("study:" + Today)) return null;
        var s = new StudyPage(Math.Clamp(p.StudyMode, 0, 2)) { _started = true, _secondsBefore = p.StudySeconds, _xpStart = p.StudyXpStart };
        // шаги, сделанные до перезапуска, могли уйти из свежего плана (например, повторять уже нечего) — показываем их как сделанные
        var gone = p.StudyDone.Where(t => s._stages.All(x => x.Title != t)).ToList();
        for (int k = gone.Count - 1; k >= 0; k--) { s._stages.Insert(0, new StudyStage("review", gone[k], "сделано раньше сегодня", 0, () => false)); s._state.Insert(0, 1); }
        for (int i = 0; i < s._stages.Count; i++)
            if (p.StudyDone.Contains(s._stages[i].Title)) s._state[i] = 1;
            else if (p.StudySkipped.Contains(s._stages[i].Title)) s._state[i] = 2;
        return s;
    }

    // План для главной: пересчитывается только после изменения прогресса (главная перерисовывается часто)
    static (int ver, int mode, List<StudyStage> plan) _planCache = (-1, -1, new());
    public static List<StudyStage> PlanCached(int mode)
    {
        if (_planCache.ver != ProgressStore.Version || _planCache.mode != mode) _planCache = (ProgressStore.Version, mode, Plan(mode));
        return _planCache.plan;
    }

    // ─────────── Подбор шагов ───────────
    public static List<StudyStage> Plan(int mode)
    {
        var p = ProgressStore.P;
        var list = new List<StudyStage>();
        int cap = mode switch { 0 => 12, 1 => 30, _ => 60 };

        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        if (due > 0)
            list.Add(new("review", "Повторение", $"{Math.Min(due, cap)} {MainView.Plural(Math.Min(due, cap), "карточка", "карточки", "карточек")} — память вот-вот начнёт их забывать", Math.Max(2, Math.Min(due, cap) / 5), () =>
            {
                var cards = Db.Words.Where(w => ProgressStore.IsDue(w.Id)).Select(FlashSession.FromWord)
                    .Concat(Db.Kanji.Where(k => ProgressStore.IsDue(k.Id)).Select(FlashSession.FromKanji))
                    .OrderBy(c => ProgressStore.Card(c.Id)?.Due).Take(cap).ToList();
                if (cards.Count == 0) return false;
                Shell.Open(new FlashSession("Повторение", cards));
                return true;
            }));

        int mist = p.Mistakes.Count;
        if (mist >= (mode == 0 ? 6 : 1))
            list.Add(new("target", "Работа над ошибками", $"{Math.Min(mist, 15)} {MainView.Plural(Math.Min(mist, 15), "место", "места", "мест")}, где ты ошибался недавно", 3, () =>
            {
                var q = TrainersPage.MistakesQuiz();
                if (q == null) return false;
                Shell.Open(q);
                return true;
            }));

        // Новое: следующий шаг пути, а если путь пройден — новые слова уровня
        var u = Course.Current;
        bool pathLeft = !Course.Complete(u);
        if (pathLeft)
        {
            int next = Enumerable.Range(0, u.Steps.Length).FirstOrDefault(i => !Course.StepDone(u, i), -1);
            bool theory = !p.Flags.Contains(Course.TheoryKey(u));
            string what = theory ? "теория юнита" : next >= 0 ? Course.StepTitle(u.Steps[next]) : u.Title;
            list.Add(new("path", "Новое по пути", $"Юнит {u.Num}: {what}", 5, () =>
            {
                var uu = Course.Current;
                int i = Enumerable.Range(0, uu.Steps.Length).FirstOrDefault(k => !Course.StepDone(uu, k), -1);
                if (!ProgressStore.P.Flags.Contains(Course.TheoryKey(uu)) || i < 0) Shell.Open(new UnitPage(uu));
                else Course.Launch(uu, i);
                return true;
            }));
        }
        // новые слова — если путь пройден, в интенсиве, или когда сессия выходит слишком короткой
        if (!pathLeft || mode == 2 || (mode == 1 && list.Sum(s => s.Minutes) < 10))
        {
            int n = mode switch { 0 => 4, 1 => 6, _ => 8 };
            list.Add(new("words", "Новые слова", $"{n} {MainView.Plural(n, "слово", "слова", "слов")} уровня {p.Level}: знакомство, потом проверка", 4, () =>
            {
                var fresh = Db.Words.Where(w => w.Level == ProgressStore.P.Level && ProgressStore.Card(w.Id) == null).Take(n).ToList();
                if (fresh.Count == 0) return false;
                Shell.Open(LearnSession.Words("Новые слова", fresh));
                return true;
            }));
        }

        // Аудирование по уже знакомым словам: проверяет, узнаёшь ли ты их на слух
        var known = Db.Words.Where(w => ProgressStore.Card(w.Id)?.Reps > 0).ToList();
        if (known.Count >= 6 && !p.QuietMode)
        {
            int n = mode switch { 0 => 5, 1 => 8, _ => 12 };
            // через день — целые фразы в обычном темпе, когда слов уже достаточно
            bool phrases = known.Count >= 20 && DateTime.Today.DayOfYear % 2 == 0;
            list.Add(new("voice", "Аудирование", phrases ? "Целые фразы в обычном темпе — без текста" : "Слова, которые ты учишь, — только на слух", 2, () =>
            {
                Shell.Open(phrases ? TrainersPage.PhraseListening(n) : ListeningQuiz(n));
                return true;
            }));
        }

        // Говорение: урок, который меньше всего практиковался
        if (Db.SpeechLessons.Length > 0 && (mode > 0 || p.Cards.Count > 20) && !p.QuietMode && !p.StudyNoSpeech)
        {
            var lesson = Db.SpeechLessons.OrderBy(l => l.Items.Count(it => p.SpeechBest.ContainsKey(it.Id)) / (double)Math.Max(1, l.Items.Length))
                .ThenBy(l => l.Id).First();
            int tried = lesson.Items.Count(it => p.SpeechBest.ContainsKey(it.Id));
            list.Add(new("speech", "Говорение", $"«{lesson.Title}» — {(tried == 0 ? "ещё не пробовал" : $"сказано {tried} из {lesson.Items.Length}")}", 4, () =>
            {
                Shell.Open(new SpeechLessonPage(lesson));
                return true;
            }));
        }

        if (mode == 2)
            list.Add(new("games", "Игра на закрепление", "Быстрый раунд на слова, которые ты учишь", 3, () =>
            {
                Shell.Open((DateTime.Today.DayOfYear % (p.QuietMode ? 2 : 3)) switch { 0 => new MatchRushGame(), 1 => (Control)new WordBuilderGame(), _ => new ListenCatchGame() });
                return true;
            }));

        if (list.Count == 0)
            list.Add(new("words", "Первые слова", "С чего начать: несколько простых слов", 4, () =>
            {
                Shell.Open(LearnSession.Words("Первые слова", Db.Words.Where(w => ProgressStore.Card(w.Id) == null).Take(5).ToList()));
                return true;
            }));
        return list;
    }

    ToggleButton Chip(string text, bool on, Action<bool> set, string tip)
    {
        var tb = new ToggleButton { Classes = { "Chip" }, Content = text, IsChecked = on }.Tip(tip);
        void Apply(bool v)
        {
            set(v); ProgressStore.Notify();
            _stages = Plan(_mode); _state.Clear(); _state.AddRange(_stages.Select(_ => 0));
            Render();
        }
        tb.Checked += (_, _) => Apply(true);
        tb.Unchecked += (_, _) => Apply(false);
        return tb;
    }

    static QuizSession ListeningQuiz(int n)
    {
        var known = Db.Words.Where(w => ProgressStore.Card(w.Id)?.Reps > 0).ToList();
        var ruPool = Db.Words.Select(w => w.Ru).ToArray();
        // сначала слабые: малая стабильность FSRS
        var pick = known.OrderBy(w => ProgressStore.Card(w.Id)!.Stability + U.Rng.NextDouble() * 3).Take(n).Shuffled();
        return new QuizSession("Аудирование", pick.Select(w => new QuizQ
        {
            Prompt = "聞いて", PromptSize = 56, Sub = "Что прозвучало? · 🔊 повторить", SpeakOnShow = true,
            Answer = w.Ru, Options = QuizSession.MakeOptions(w.Ru, ruPool), Speak = w.Kana, Key = w.Id,
            Explain = $"{w.Jp} — {w.Kana} ({w.Romaji})",
        }));
    }

    // ─────────── Экран ───────────
    public void OnShown()
    {
        if (_launched)
        {
            _launched = false;
            _state[_cur] = 1;
            Save();
            Advance(auto: true);
            return;
        }
        Render();
    }

    void Advance(bool auto)
    {
        int next = _state.IndexOf(0);
        if (next < 0) { Finish(); return; }
        Render(justDone: _cur);
        if (!auto) { Launch(next); return; }
        // короткая пауза между шагами: видно, что шаг засчитан, и что будет дальше
        _auto?.Stop();
        _auto = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TransitionMs) };
        _auto.Tick += (_, _) => { _auto?.Stop(); if (_host.IsAttached()) Launch(_state.IndexOf(0)); };
        _auto.Start();
    }

    const int TransitionMs = 1800;

    void Launch(int i)
    {
        _auto?.Stop();
        if (i < 0) { Finish(); return; }
        if (!_started) { _started = true; _xpStart = ProgressStore.P.Xp; }
        if (!_clock.IsRunning) _clock.Start();
        _cur = i;
        if (_stages[i].Launch()) { _launched = true; Save(); }
        else { _state[i] = 2; Save(); Advance(auto: false); }
    }

    readonly Grid _host = new();

    void Render(int justDone = -1)
    {
        var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center };
        bool started = _started;
        int done = _state.Count(s => s != 0);
        int nextIdx = _state.IndexOf(0);

        // Шапка: маскот + что происходит
        var head = U.Cols("auto", "*");
        // следующий шаг — работа над ошибками: Википе-тан со шваброй «наводит порядок»
        bool sweepNext = nextIdx >= 0 && _stages[nextIdx].Icon == "target" && started;
        head.Children.Add((sweepNext ? Art.Anime("sweep", 130) : Art.Mascot(Shell.Compact ? 90 : 130, started ? Mood.Joy : Mood.Happy)).DesktopOnly());
        var say = U.Stack(
            U.Jp(started ? "その調子！" : "勉強しよう！", 15, U.B("Accent")),
            U.T(started ? $"Шаг {done} из {_stages.Count} готов" : "Сессия собрана под тебя", "H1", Shell.Compact ? 22 : 28).M(0, 2, 0, 6),
            U.T(started ? (nextIdx >= 0 ? $"Дальше — {_stages[nextIdx].Title.ToLower()}…" : "") : $"{_stages.Count} {MainView.Plural(_stages.Count, "шаг", "шага", "шагов")} · около {_stages.Sum(s => s.Minutes)} минут · по твоей статистике и расписанию повторений", "Muted", 13.5)
                .Also(t => t.TextWrapping = TextWrapping.Wrap));
        head.Children.Add(say.CenterV().M(Shell.Compact ? 0 : 20, 0, 0, 0).Col(1));
        root.Children.Add(head);

        // Выбор длины — только до старта
        if (!started)
        {
            var modes = new TileGrid { MinTile = 96, MaxColumns = 3, Margin = new Thickness(-5, 20, -5, 0) }; // узкий экран — режимы в две строки, слова не рвутся
            for (int m = 0; m < Modes.Length; m++)
            {
                int mm = m;
                bool on = m == _mode;
                var b = new Border
                {
                    Margin = new Thickness(5), Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
                    Background = on ? U.B("AccentSoft") : U.B("Surface"), BorderBrush = on ? U.B("Accent") : U.B("Stroke"), BorderThickness = new Thickness(on ? 1.5 : 1),
                    Child = U.Stack(U.T(Modes[m].name, "H3", 14).Center(), U.T(Modes[m].sub, "Muted", 12).Center()),
                };
                b.PointerReleased += (_, _) =>
                {
                    _mode = mm; ProgressStore.P.StudyMode = mm; ProgressStore.Notify();
                    _stages = Plan(mm); _state.Clear(); _state.AddRange(_stages.Select(_ => 0));
                    Render();
                };
                modes.Children.Add(b);
            }
            root.Children.Add(modes);
            // состав: например, в транспорте — без говорения или совсем без звука
            var opts = U.Row(8,
                Chip("Без говорения", ProgressStore.P.StudyNoSpeech, v => ProgressStore.P.StudyNoSpeech = v, "Сегодня без микрофона: шаг «Говорение» не добавляется"),
                Chip("Тихо: без звука", ProgressStore.P.QuietMode, v => ProgressStore.P.QuietMode = v, "Без автоозвучки, аудирования и говорения — только текстовые задания"));
            root.Children.Add(opts.M(0, 10, 0, 0));
        }

        // Маршрут: вертикальная линия с шагами
        var route = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            int st = _state[i];
            bool isNext = i == nextIdx;
            var row = U.Cols("44", "*", "auto");
            var dot = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0),
                Background = st == 1 ? U.B("Good") : isNext ? U.B("Accent") : U.B("Surface2"),
                Child = new TextBlock
                {
                    Text = st == 1 ? "" : st == 2 ? "" : (i + 1).ToString(), FontFamily = st != 0 ? U.R<FontFamily>("Icons") : FontFamily.Default,
                    FontSize = st != 0 ? 12 : 13, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = st == 1 || isNext ? U.B("OnAccent") : U.B("Muted"),
                },
            };
            var line = new Rectangle { Width = 2, Fill = st == 1 ? U.B("Good") : U.B("Stroke"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 46, 0, -14), IsVisible = i < _stages.Count - 1 };
            row.Children.Add(new Grid { Children = { line, dot } });
            var g = U.Cols("auto", "*");
            g.Children.Add(Art.Icon(s.Icon, 40).CenterV());
            g.Children.Add(U.Stack(U.T(s.Title, "H3", 15, st != 0 ? U.B("Muted") : null), U.T(s.Why, "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap))
                .CenterV().M(14, 0, 0, 0).Col(1));
            var card = new Border
            {
                Classes = { "Card" }, Padding = new Thickness(16, 12), Margin = new Thickness(6, 6, 0, 6), Child = g, Opacity = st != 0 ? 0.6 : 1,
                BorderBrush = isNext && started ? U.B("Accent") : U.B("Stroke"),
            };
            if (st == 0 && started)
            {
                int ii = i;
                card.Cursor = Cursors.Hand;
                card.PointerReleased += (_, _) => Launch(ii);
            }
            row.Children.Add(card.Col(1));
            row.Children.Add(U.T(st == 2 ? "пропущено" : $"{s.Minutes} мин", "Muted", 12).CenterV().M(12, 0, 4, 0).Col(2).DesktopOnly());
            route.Children.Add(row);
            if (i == justDone)
            {
                // засчитанный шаг: точка «выпрыгивает», карточка гаснет
                Anim.Pop(dot, 0.4); Sfx.Ok();
                Anim.Opacity(card, 1, 0.6, 500, 150);
            }
            if (justDone >= 0 && isNext)
            {
                // следующий шаг выезжает справа и подсвечивается
                var tt = new TranslateTransform(40, 0); card.RenderTransform = tt;
                Anim.Run((tt, "x"), 40, 0, 520, v => tt.X = v, Ease.QuinticOut, 220);
                Anim.Opacity(card, 0, 1, 420, 220);
            }
        }
        root.Children.Add(route);

        var buttons = U.Row(10);
        buttons.Margin = new Thickness(0, 20, 0, 0);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        if (!started) buttons.Children.Add(U.Btn("Начать", () => Launch(0), "Primary", "").Also(b => b.MinWidth = 200));
        else if (nextIdx >= 0)
        {
            buttons.Children.Add(U.Btn("Дальше", () => Launch(_state.IndexOf(0)), "Primary", "").Also(b => b.MinWidth = 180));
            buttons.Children.Add(U.Btn("Пропустить шаг", () => { _auto?.Stop(); _state[nextIdx] = 2; Advance(auto: false); }, "Ghost", ""));
        }
        buttons.Children.Add(U.Btn(started ? "Пауза" : "Назад", () => { _auto?.Stop(); _clock.Stop(); if (_started) Save(); Shell.Back(); }, "Ghost", ""));
        root.Children.Add(buttons);
        if (started && justDone >= 0 && nextIdx >= 0)
        {
            // полоска отсчёта до автоматического перехода
            var track = new Border { Height = 4, Width = 260, CornerRadius = new CornerRadius(2), Background = U.B("Surface2"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            var fill = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = U.B("AccentGradient"), HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            root.Children.Add(track);
            Anim.Run((fill, "w"), 0, 260, TransitionMs - 100, v => fill.Width = v, Ease.Linear);
        }
        if (started) root.Children.Add(U.T(Shell.Compact ? "Следующий шаг откроется сам" : "Следующий шаг откроется сам · Пробел — сразу", "Muted", 12).Center().M(0, 10, 0, 0));

        _host.Children.Clear();
        _host.Children.Add(root);
        Content = U.Page(_host.Detach().M(Shell.Compact ? 0 : 24, 8, Shell.Compact ? 0 : 24, 24));
        Anim.FadeUp(root, 0, 10, 320);
        if (justDone < 0) Anim.Stagger(route, 50, 80);
    }

    void Finish()
    {
        _finished = true;
        _clock.Stop();
        _auto?.Stop();
        int xp = ProgressStore.P.Xp - _xpStart;
        int done = _state.Count(s => s == 1);
        int min = Math.Max(1, (int)Math.Round(Seconds / 60.0));
        ProgressStore.P.Flags.Add("study:" + DateTime.Today.ToString("yyyy-MM-dd"));
        ProgressStore.P.StudySessions++;
        ProgressStore.P.StudyDay = "";
        Challenges.Bump("study");
        ProgressStore.Notify();
        Sfx.Done();
        var grid = new Grid();
        grid.Children.Add(Results.Build(100 * done / Math.Max(1, _stages.Count), Math.Max(0, xp), new[]
        {
            ("Шагов пройдено", $"{done} из {_stages.Count}"),
            ("Время", $"{min} мин"),
            ("Серия", $"{ProgressStore.P.Streak} {MainView.Plural(ProgressStore.P.Streak, "день", "дня", "дней")}"),
            ("Сегодня", $"{ProgressStore.TodayXp} из {ProgressStore.P.DailyGoal} XP"),
        }, null, () => { Active = null; Shell.Go("home"); }, grid, "сессия"));
        Content = grid;
    }

    public bool HandleKey(Avalonia.Input.Key key)
    {
        if (_finished || key is not (Avalonia.Input.Key.Space or Avalonia.Input.Key.Enter)) return false;
        Launch(_state.IndexOf(0));
        return true;
    }
}

static class AttachExt
{
    public static bool IsAttached(this Visual v) => TopLevel.GetTopLevel(v) != null;
}
