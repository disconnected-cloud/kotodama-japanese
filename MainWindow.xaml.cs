using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Matsubakira.Services;
using Matsubakira.Views;

namespace Matsubakira;

public partial class MainWindow : Window
{
    readonly Dictionary<string, FrameworkElement> _roots = new();
    readonly List<FrameworkElement> _stack = new();
    readonly TranslateTransform _hostShift = new();

    public MainWindow()
    {
        InitializeComponent();
        Shell.Main = this;
#if DEBUG
        if (Environment.GetCommandLineArgs().Any(a => a is "--snap" or "--selftest" or "--offscreen"))
        {
            ShowActivated = false; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -20000; Top = 0;
        }
#endif
        Host.RenderTransform = _hostShift;
        if (Math.Abs(ProgressStore.P.UiScale - 1) > 0.01) Root.LayoutTransform = new ScaleTransform(ProgressStore.P.UiScale, ProgressStore.P.UiScale);
        StateChanged += (_, _) => OnStateChanged();
        ProgressStore.Changed += UpdateStats;
        Loaded += (_, _) =>
        {
            SpawnPetals();
            UpdateStats();
            NavHome.IsChecked = true;
            PlayIntro();
            Tray.Init(this);
            if (!ProgressStore.P.Flags.Contains("onboarded") && !Environment.GetCommandLineArgs().Skip(1).Any()) Push(new OnboardingPage());
            if (!Environment.GetCommandLineArgs().Skip(1).Any())
            {
                Updater.AutoCheck();
                if (CrashReport.HasNew()) { CrashReport.MarkSeen(); Shell.Toast("В прошлый раз была ошибка — отправить отчёт можно в Настройках → «Обратная связь»", "!"); }
            }
            var remind = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            remind.Tick += (_, _) => Tray.Tick();
            remind.Start();
            var args = Environment.GetCommandLineArgs();
            int pi = Array.IndexOf(args, "--page");
            if (pi > 0 && pi + 1 < args.Length) NavigateRoot(args[pi + 1]);
#if DEBUG
            int oi = Array.IndexOf(args, "--open");
            if (oi > 0 && oi + 1 < args.Length) DebugOpen(args[oi + 1]);
            int ti = Array.IndexOf(args, "--selftest");
            if (ti > 0 && ti + 1 < args.Length) DebugSelfTest(args[ti + 1]);
            int si = Array.IndexOf(args, "--snap");
            if (si > 0 && si + 1 < args.Length) DebugSnap(args[si + 1]);
#endif
        };
        PreviewKeyDown += OnPreviewKey;
        Activated += (_, _) => UpdateStats();
    }

    // ─────────── Навигация ───────────
    static readonly Dictionary<string, Func<FrameworkElement>> Factories = new()
    {
        ["home"] = () => new HomePage(),
        ["path"] = () => new PathPage(),
        ["dialogs"] = () => new DialogsPage(),
        ["dict"] = () => new DictPage(),
        ["kana"] = () => new KanaPage(),
        ["words"] = () => new WordsPage(),
        ["kanji"] = () => new KanjiPage(),
        ["grammar"] = () => new GrammarPage(),
        ["speech"] = () => new SpeechPage(),
        ["review"] = ReviewPage.Create,
        ["settings"] = () => new SettingsPage(),
        ["trainers"] = () => new TrainersPage(),
        ["reader"] = () => new ReaderPage(),
    };

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        var key = sender switch
        {
            _ when sender == NavPath => "path",
            _ when sender == NavDialogs => "dialogs",
            _ when sender == NavDict => "dict",
            _ when sender == NavKana => "kana",
            _ when sender == NavWords => "words",
            _ when sender == NavKanji => "kanji",
            _ when sender == NavGrammar => "grammar",
            _ when sender == NavSpeech => "speech",
            _ when sender == NavReview => "review",
            _ when sender == NavSettings => "settings",
            _ when sender == NavTrainers => "trainers",
            _ when sender == NavReader => "reader",
            _ => "home",
        };
        MoveNavIndicator((FrameworkElement)sender);
        ShowRoot(key);
    }

    /// <summary>Подложка выбранного пункта плавно «переезжает» к новому пункту.</summary>
    void MoveNavIndicator(FrameworkElement item)
    {
        if (!item.IsLoaded) { item.Loaded += (_, _) => MoveNavIndicator(item); return; }
        var y = item.TransformToAncestor(Nav).Transform(new Point(0, 0)).Y;
        var first = NavIndicator.Opacity == 0;
        NavIndicator.Opacity = 1;
        NavShift.BeginAnimation(TranslateTransform.YProperty, first ? null : Anim.Da(null, y, 320, new QuinticEase { EasingMode = EasingMode.EaseOut }));
        if (first) NavShift.Y = y;
    }

    public void NavigateRoot(string key)
    {
        var rb = key switch
        {
            "kana" => NavKana, "words" => NavWords, "kanji" => NavKanji, "grammar" => NavGrammar,
            "speech" => NavSpeech, "review" => NavReview, "settings" => NavSettings,
            "path" => NavPath, "dialogs" => NavDialogs, "dict" => NavDict, "trainers" => NavTrainers, "reader" => NavReader, _ => NavHome,
        };
        if (rb.IsChecked == true) ShowRoot(key); else rb.IsChecked = true;
    }

    void ShowRoot(string key)
    {
        // Страница повторения всегда пересоздаётся — очередь зависит от времени
        if (key == "review" || !_roots.TryGetValue(key, out var page))
        {
            page = Factories[key]();
            if (key != "review") _roots[key] = page;
        }
        _stack.Clear();
        _stack.Add(page);
        Show(page, 1);
    }

    public FrameworkElement? PendingExpand;

    public void Push(FrameworkElement page)
    {
        if (PendingExpand is { } src) { PendingExpand = null; ExpandFrom(src, page); return; }
        _stack.Add(page);
        Show(page, 1);
    }

    public void Pop()
    {
        if (_stack.Count <= 1) { NavigateRoot("home"); return; }
        _stack.RemoveAt(_stack.Count - 1);
        Show(_stack[^1], -1);
    }

    void Show(FrameworkElement page, int dir)
    {
        Host.Content = page;
        // Режим фокуса: во время упражнений боковое меню скрывается
        bool focus = page is Controls.QuizSession or Controls.FlashSession or BuilderSession or SpeechLessonPage or DialogPage or WritingPage or KanaRainGame or MemoryGame or SprintGame or OnboardingPage;
        SideCol.Width = new GridLength(focus ? 0 : 232);
        Sidebar.Visibility = focus ? Visibility.Collapsed : Visibility.Visible;
        BackBtn.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        CrumbText.Text = string.Join("  ›  ", _stack.OfType<IPage>().Select(p => p.Title));
        (page as IPage)?.OnShown();

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        Host.BeginAnimation(OpacityProperty, Anim.Da(0, 1, 220));
        _hostShift.BeginAnimation(TranslateTransform.YProperty, Anim.Da(dir > 0 ? 10 : -10, 0, 380, ease));
        UpdateStats();
    }

    void Back_Click(object sender, RoutedEventArgs e) => Pop();

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (Intro.Visibility == Visibility.Visible) { CloseIntro(fast: true); e.Handled = true; return; }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { OpenPalette(); e.Handled = true; return; }
        if (_palette != null) return;
        if (Keyboard.FocusedElement is TextBox && e.Key is not (Key.Enter or Key.Escape)) return;
        if (Host.Content is IKeyTarget kt && kt.HandleKey(e.Key)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && _stack.Count > 1) { Pop(); e.Handled = true; }
        else if (e.Key == Key.Back && _stack.Count > 1 && Keyboard.FocusedElement is not TextBox) { Pop(); e.Handled = true; }
    }

    // ─────────── Статистика в сайдбаре ───────────
    void UpdateStats()
    {
        Achievements.CheckNew();
        var p = ProgressStore.P;
        StreakText.Text = $"{p.Streak} {Plural(p.Streak, "день", "дня", "дней")}";
        int lvl = ProgressStore.Level;
        LevelText.Text = $"Ур. {lvl}";
        int from = ProgressStore.XpForLevel(lvl), to = ProgressStore.XpForLevel(lvl + 1);
        XpBar.Maximum = Math.Max(1, to - from);
        XpBar.Value = p.Xp - from;
        XpText.Text = $"{p.Xp - from} / {to - from} XP";
        GoalBar.Maximum = p.DailyGoal;
        GoalBar.Value = Math.Min(p.DailyGoal, ProgressStore.TodayXp);
        GoalText.Text = $"{ProgressStore.TodayXp} / {p.DailyGoal} XP";

        int due = p.Cards.Count(kv => kv.Value.Due <= DateTime.Now);
        DueBadge.Visibility = due > 0 ? Visibility.Visible : Visibility.Collapsed;
        DueText.Text = due > 99 ? "99+" : due.ToString();
    }

    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    // ─────────── Тост ───────────
    public void ShowToast(string text, string? jp)
    {
        ToastText.Text = text;
        ToastJp.Text = jp ?? "";
        ToastJp.Visibility = string.IsNullOrEmpty(jp) ? Visibility.Collapsed : Visibility.Visible;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2800))));
        Toast.BeginAnimation(OpacityProperty, a);
        Anim.Pop(Toast, 0.9);
    }

    // ─────────── Лепестки сакуры ───────────
    // Каждый лепесток кэшируется как GPU-текстура (BitmapCache), анимируются только трансформации —
    // это почти бесплатно для CPU даже на слабых машинах.
    static readonly Geometry PetalGeo = Freeze(Geometry.Parse("M0,8 C2,-2 14,-3 18,4 C20,8 16,14 9,14 C4,14 -1,12 0,8 Z"));
    static Geometry Freeze(Geometry g) { g.Freeze(); return g; }
    readonly List<Path> _petals = new();

    public void SetPetals(bool on)
    {
        // Останавливаем часы анимаций явно — иначе они тикают и после удаления элементов
        foreach (var p in _petals)
        {
            if (p.RenderTransform is TransformGroup g && g.Children.Count == 3)
            {
                g.Children[0].BeginAnimation(RotateTransform.AngleProperty, null);
                g.Children[1].BeginAnimation(ScaleTransform.ScaleXProperty, null);
                g.Children[2].BeginAnimation(TranslateTransform.XProperty, null);
                g.Children[2].BeginAnimation(TranslateTransform.YProperty, null);
            }
            Petals.Children.Remove(p);
        }
        _petals.Clear();
        if (on) SpawnPetals();
    }

    static T Fps<T>(T t) where T : Timeline { Timeline.SetDesiredFrameRate(t, 24); return t; }

    void SpawnPetals()
    {
        if (!ProgressStore.P.Petals || _petals.Count > 0 || WindowState == WindowState.Minimized || Environment.GetCommandLineArgs().Contains("--nopetals")) return;
        if ((RenderCapability.Tier >> 16) == 0) return; // без аппаратного ускорения декор слишком дорогой
        var rng = new Random();
        var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xB7, 0xCF), Color.FromRgb(0xFF, 0x7A, 0xA8), 45);
        fill.Freeze();
        for (int i = 0; i < 8; i++)
        {
            double scale = 0.5 + rng.NextDouble() * 0.9;
            var rot = new RotateTransform(rng.Next(360), 9, 7);
            var sc = new ScaleTransform(scale, scale);
            var tr = new TranslateTransform();
            var p = new Path
            {
                Data = PetalGeo, Fill = fill, Opacity = 0.25 + rng.NextDouble() * 0.45,
                RenderTransform = new TransformGroup { Children = { rot, sc, tr } },
                CacheMode = new BitmapCache(1.5),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(p, rng.NextDouble() * 1500);
            Canvas.SetTop(p, -40);
            Petals.Children.Add(p);
            _petals.Add(p);

            double dur = 14 + rng.NextDouble() * 14;
            var begin = TimeSpan.FromSeconds(-rng.NextDouble() * dur); // уже «в полёте» при старте
            // 30 fps достаточно для медленного падения и вдвое дешевле 60
            var fall = Fps(new DoubleAnimation(0, 1100, TimeSpan.FromSeconds(dur)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            var drift = Fps(new DoubleAnimation(0, -160 - rng.NextDouble() * 220, TimeSpan.FromSeconds(dur)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            var spin = Fps(new DoubleAnimation(rot.Angle, rot.Angle + (rng.Next(2) == 0 ? 360 : -360), TimeSpan.FromSeconds(6 + rng.NextDouble() * 8)) { RepeatBehavior = RepeatBehavior.Forever });
            var flutter = Fps(new DoubleAnimation(scale, scale * 0.35, TimeSpan.FromSeconds(1.6 + rng.NextDouble()))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });

            tr.BeginAnimation(TranslateTransform.YProperty, fall);
            tr.BeginAnimation(TranslateTransform.XProperty, drift);
            rot.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
    }

#if DEBUG
    // Рендер собственного окна в PNG (без захвата экрана) и выход — для визуальной проверки
    void DebugSnap(string path)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle((Brush)FindResource("Bg"), null, new Rect(0, 0, ActualWidth, ActualHeight));
            rtb.Render(bg);
            rtb.Render(Root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
            Application.Current.Shutdown();
        };
        t.Start();
    }

    /// <summary>Проверка цепочки без микрофона: нейро-голос → WAV 16 кГц → Whisper → оценка; плюс поиск по словарю.</summary>
    async void DebugSelfTest(string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        try
        {
            L("romaji: " + Db.RomajiFor("私は学生です。", "わたしはがくせいです。") + " | " + Kana.FromRomaji("taberu") + " " + Kana.FromRomaji("gakkou") + " " + Kana.FromRomaji("konnichiwa"));
            L($"data: words={Db.Words.Length} kanji={Db.Kanji.Length} grammar={Db.Grammar.Length} dialogs={Db.Dialogs.Length} course={Db.Course.Length} strokes(日)={Db.Strokes("日")?.Length}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await JDict.LoadAsync();
            L($"dict loaded {JDict.Count} in {sw.ElapsedMilliseconds} ms");
            foreach (var q in new[] { "кошка", "neko", "たべる", "猫", "учиться" })
            {
                sw.Restart();
                var r = JDict.Search(q);
                L($"  search '{q}': {r.Count} results in {sw.ElapsedMilliseconds} ms; top: " + string.Join(" | ", r.Take(3).Select(e => $"{e.Jp}({e.Kana}) {e.Ru[..Math.Min(30, e.Ru.Length)]}")));
            }
            foreach (var (jp, kana) in new[] { ("おはようございます", "おはようございます"), ("駅はどこですか", "えきはどこですか"), ("日本語を勉強しています", "にほんごをべんきょうしています") })
            {
                sw.Restart();
                var mp3 = await Tts.GetAsync(jp);
                L($"tts '{jp}': {(mp3 == null ? "FAIL " + Tts.LastError : new System.IO.FileInfo(mp3).Length + " bytes")} in {sw.ElapsedMilliseconds} ms");
                if (mp3 == null || !Asr.Ready) continue;
                byte[] wav;
                using (var reader = new NAudio.Wave.MediaFoundationReader(mp3))
                using (var res = new NAudio.Wave.MediaFoundationResampler(reader, new NAudio.Wave.WaveFormat(16000, 16, 1)))
                using (var ms = new System.IO.MemoryStream())
                {
                    var buf = new byte[8192]; int n;
                    while ((n = res.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    wav = Recorder.ToWav(ms.ToArray());
                }
                sw.Restart();
                var (text, conf) = await Asr.TranscribeAsync(wav, CancellationToken.None);
                var sc = Speech.Score(text, conf, jp, kana);
                L($"  whisper: '{text}' conf={conf:0.00} score={sc.Score} in {sw.ElapsedMilliseconds} ms");
            }
            L("mic devices: " + (Recorder.HasMicrophone ? "yes" : "no"));
            var pa = await Tts.GetAsync("おはようございます"); var pb = await Tts.GetAsync("駅はどこですか"); var pc = await Tts.GetAsync("おはようございます", true);
            if (pa != null && pb != null && pc != null)
            {
                sw.Restart();
                var A = Prosody.DecodeFile(pa); var B = Prosody.DecodeFile(pb); var C = Prosody.DecodeFile(pc);
                var same = Prosody.Compare(A, A); var slow = Prosody.Compare(A, C); var diff = Prosody.Compare(A, B);
                L($"prosody same: inton={same?.Intonation} rhythm={same?.Rhythm} | slow-version: inton={slow?.Intonation} rhythm={slow?.Rhythm} ratio={slow?.DurationRatio:0.00} | other phrase: inton={diff?.Intonation} rhythm={diff?.Rhythm}  ({sw.ElapsedMilliseconds} ms)");
                L("  tips(slow): " + string.Join(" / ", slow?.Tips ?? new()));
            }
            L($"voicevox catalog packs={Voicevox.Catalog.Length} installed={Voicevox.Installed}");
            // FSRS: новая карточка «Хорошо» несколько раз подряд — интервалы должны расти
            var fc = new SrsCard(); var seq = new List<string>();
            for (int k = 0; k < 5; k++) { var (s1, d1, days) = Fsrs.Next(fc, Rating.Good); seq.Add($"{days:0}d"); Fsrs.Apply(fc, Rating.Good); fc.LastReview = DateTime.Now.AddDays(-days); }
            var (_, _, againDays) = Fsrs.Next(fc, Rating.Again);
            L("fsrs good x5: " + string.Join(" ", seq) + $" | again -> {againDays} | S={fc.Stability:0.0} D={fc.Difficulty:0.0}");
            var conj = Views.Conj.All("食べる", "たべる")!; var conj2 = Views.Conj.All("行く", "いく")!; var conj3 = Views.Conj.All("飲む", "のむ")!;
            L($"conj: {conj["te"]} {conj["nai"]} {conj["pot"]} | {conj2["te"]} {conj2["ta"]} | {conj3["te"]} {conj3["vol"]} {conj3["ba"]}");
            var (len, e) = JDict.Longest("日本語を勉強します", 0);
            L($"longest match: '{"日本語を勉強します".Substring(0, len)}' -> {e?.Ru}");
        }
        catch (Exception ex) { L("ERROR: " + ex); }
        L("done");
        Application.Current.Shutdown();
    }

    void DebugOpen(string what)
    {
        switch (what)
        {
            case "flash": Push(new Controls.FlashSession("Демо", Db.Words.Take(5).Select(Controls.FlashSession.FromWord))); break;
            case "kanjiflash": Push(new Controls.FlashSession("Демо", Db.Kanji.Take(5).Select(Controls.FlashSession.FromKanji))); break;
            case "grammar": Push(new GrammarLessonPage(Db.Grammar[4])); break;
            case "grammar1": Push(new GrammarLessonPage(Db.Grammar[0])); break;
            case "onboard": Push(new OnboardingPage()); break;
            case "speech": Push(new SpeechLessonPage(Db.SpeechLessons[1])); break;
            case "dialog": Push(new DialogPage(Db.Dialogs[1])); break;
            case "unit": Push(new UnitPage(Db.Course[0])); break;
            case "reader": Push(new ReaderPage()); break;
            case "writing": Push(new WritingPage()); break;
            case "mic": Push(new MicCheckPage()); break;
            case "quiz":
                var w = Db.Words.Take(8).ToArray();
                Push(new Controls.QuizSession("Демо", w.Select(x => new Controls.QuizQ { Prompt = x.Jp, Sub = x.Kana, Answer = x.Ru, Options = Controls.QuizSession.MakeOptions(x.Ru, w.Select(y => y.Ru)) })));
                break;
        }
    }
#endif

    // ─────────── «Раскрытие» карточки в страницу ───────────
    bool _expanding;
    public void ExpandFrom(FrameworkElement source, FrameworkElement page)
    {
        if (_expanding || !source.IsVisible) { Push(page); return; }
        _expanding = true;
        Rect from, to;
        try
        {
            from = source.TransformToAncestor(Root).TransformBounds(new Rect(source.RenderSize));
            to = Host.TransformToAncestor(Root).TransformBounds(new Rect(Host.RenderSize));
        }
        catch { _expanding = false; Push(page); return; }
        var ghost = new Border
        {
            Width = to.Width, Height = to.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(to.X, to.Y, 0, 0), Background = U.B("Surface"), BorderBrush = U.B("StrokeStrong"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), IsHitTestVisible = false, RenderTransformOrigin = new Point(0, 0),
        };
        var sc = new ScaleTransform(from.Width / to.Width, from.Height / to.Height);
        var tr = new TranslateTransform(from.X - to.X, from.Y - to.Y);
        ghost.RenderTransform = new TransformGroup { Children = { sc, tr } };
        Root.Children.Add(ghost);
        var ease = new QuinticEase { EasingMode = EasingMode.EaseInOut };
        const int ms = 300;
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.Da(null, 1, ms, ease));
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.Da(null, 1, ms, ease));
        tr.BeginAnimation(TranslateTransform.XProperty, Anim.Da(null, 0, ms, ease));
        var last = Anim.Da(null, 0, ms, ease);
        last.Completed += (_, _) =>
        {
            Push(page);
            var fade = Anim.Da(1, 0, 220);
            fade.Completed += (_, _) => { Root.Children.Remove(ghost); _expanding = false; };
            ghost.BeginAnimation(OpacityProperty, fade);
        };
        tr.BeginAnimation(TranslateTransform.YProperty, last);
    }

    // ─────────── Ctrl+K ───────────
    Controls.Palette? _palette;
    public void OpenPalette()
    {
        if (_palette != null) return;
        _palette = new Controls.Palette(() => { Root.Children.Remove(_palette); _palette = null; });
        Root.Children.Add(_palette);
    }

    // ─────────── Интро ───────────
    bool _introClosing;

    /// <summary>Черты 言霊 прорисовываются кистью, затем появляется название, и экран растворяется (~3 с, пропуск — клик/клавиша).</summary>
    void PlayIntro()
    {
        var cl = Environment.GetCommandLineArgs();
        if (!cl.Contains("--intro") && (!ProgressStore.P.Intro || cl.Any(a => a.StartsWith("--")))) return;
        Intro.Visibility = Visibility.Visible;
        Intro.Opacity = 1;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var s1 = new Controls.StrokeView("言", 150, plain: true, ink: U.B("Accent"), thick: 4.4);
        var s2 = new Controls.StrokeView("霊", 150, plain: true, ink: U.B("Text"), thick: 4.4) { Margin = new Thickness(14, 0, 0, 0) };
        row.Children.Add(s1); row.Children.Add(s2);
        stack.Children.Add(row);
        var title = new TextBlock { Text = "Kotodama", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = U.B("Text"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0), Opacity = 0 };
        var sub = new TextBlock { Text = "言霊 — душа слова · японский шаг за шагом", FontSize = 14, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Opacity = 0 };
        var line = new Border { Height = 2, Width = 0, CornerRadius = new CornerRadius(1), Background = U.B("Accent"), Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(title); stack.Children.Add(sub); stack.Children.Add(line);
        Intro.Children.Clear();
        Intro.Children.Add(stack);
        Intro.MouseLeftButtonDown += (_, _) => CloseIntro(fast: true);

        double end = Math.Max(s1.Play(2.6, 250), s2.Play(3.2, 550));
        Anim.FadeUp(title, (int)Math.Max(0, end - 350), 14, 520);
        Anim.FadeUp(sub, (int)Math.Max(0, end - 180), 10, 520);
        line.BeginAnimation(WidthProperty, Anim.Da(0, 120, 600, new QuinticEase { EasingMode = EasingMode.EaseOut }, (int)end));
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(end + 900) };
        t.Tick += (_, _) => { t.Stop(); CloseIntro(fast: false); };
        t.Start();
    }

    void CloseIntro(bool fast)
    {
        if (_introClosing || Intro.Visibility != Visibility.Visible) return;
        _introClosing = true;
        var fade = Anim.Da(1, 0, fast ? 220 : 520, new CubicEase { EasingMode = EasingMode.EaseInOut });
        fade.Completed += (_, _) => { Intro.Visibility = Visibility.Collapsed; Intro.Children.Clear(); };
        if (Intro.Children.Count > 0 && Intro.Children[0] is FrameworkElement st)
        {
            var sc = new ScaleTransform(1, 1);
            st.RenderTransform = sc;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.Da(1, 1.05, 520));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.Da(1, 1.05, 520));
        }
        Intro.BeginAnimation(OpacityProperty, fade);
        // Страница под интро мягко «проявляется»
        Host.BeginAnimation(OpacityProperty, Anim.Da(0, 1, 500));
    }

    // ─────────── Окно ───────────
    void OnStateChanged()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        // Свёрнутое окно не тратит CPU на анимации фона
        if (WindowState == WindowState.Minimized) SetPetals(false);
        else SpawnPetals();
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
    }
    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();
}
