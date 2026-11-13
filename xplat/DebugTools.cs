#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Matsubakira.Services;
using Matsubakira.Views;
using RadioButton = Matsubakira.Compat.RadioButton;

namespace Matsubakira;

/// <summary>Отладочные флаги командной строки (только сборка Debug): снимки экрана, бенчмарки речи, проверки.</summary>
public partial class MainView
{
    void DebugArgs(string[] args)
    {
        int rr = Array.IndexOf(args, "--rerender");
        if (rr > 0 && rr + 1 < args.Length) DispatcherTimer.RunOnce(() => DebugRerender(args[rr + 1]), TimeSpan.FromSeconds(1));
        int oi = Array.IndexOf(args, "--open");
        if (oi > 0 && oi + 1 < args.Length) DebugOpen(args[oi + 1]);
#if !MOBILE
        int ru = Array.IndexOf(args, "--rutts");
        if (ru > 0 && ru + 1 < args.Length)
        {
            var outRu = args[ru + 1];
            ProgressStore.ReadOnly = true;
            ProgressStore.P.TtsEngine = "online"; // проверяем именно Edge, не VOICEVOX
            Task.Run(async () =>
            {
                var r = await Tts.GetRuAsync("Проверка русского голоса");
                var e1 = Tts.LastError;
                var j = await Tts.GetAsync("テスト" + DateTime.Now.Ticks % 1000);
                System.IO.File.WriteAllText(outRu, $"ru={r ?? "null"} {e1} | ja={j ?? "null"} {Tts.LastError}");
                Environment.Exit(0);
            });
        }
        int pt = Array.IndexOf(args, "--playtest");
        if (pt > 0 && pt + 1 < args.Length) DebugPlay(args[pt + 1]);
#endif
        if (args.Contains("--ctrlk")) // проверка Ctrl+K без фокуса: событие приходит прямо в окно
            DispatcherTimer.RunOnce(() => TopLevel.GetTopLevel(this)?.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = KeyDownEvent, Key = Key.K, PhysicalKey = PhysicalKey.K, KeyModifiers = KeyModifiers.Control, Source = TopLevel.GetTopLevel(this),
            }), TimeSpan.FromSeconds(1));
        int vr = Array.IndexOf(args, "--vvreadings");
        if (vr > 0 && vr + 1 < args.Length)
            Task.Run(() =>
            {
                var texts = Db.SpeechLessons.SelectMany(l => l.Items.Select(i => i.Jp)).Concat(Db.Dialogs.SelectMany(d => d.Lines.Select(l => l.Jp))).Distinct().ToList();
                var bad = Voicevox.ReadingMismatches(texts);
                System.IO.File.WriteAllLines(args[vr + 1], bad.Prepend($"{bad.Count} из {texts.Count}"));
                Environment.Exit(0);
            });
        int nb = Array.IndexOf(args, "--noisebench");
        if (nb > 0 && nb + 1 < args.Length) DebugNoiseBench(args[nb + 1]);
        int bi = Array.IndexOf(args, "--asrbench");
        if (bi > 0 && bi + 2 < args.Length) DebugAsrBench(args[bi + 1], args[bi + 2]);
        int ct = Array.IndexOf(args, "--cliptest");
        if (ct > 0 && ct + 1 < args.Length) DebugClipTest(args[ct + 1]);
        if (args.Contains("--vvtest")) DebugVoicevoxTest();
        int pf = Array.IndexOf(args, "--perf");
        if (pf > 0 && pf + 1 < args.Length) DebugPerf(args[pf + 1]);
        int si = Array.IndexOf(args, "--snap");
        if (si > 0 && si + 1 < args.Length) DebugSnap(args[si + 1]);
    }

    /// <summary>
    /// Проверка VOICEVOX от начала до конца (для эмулятора Android в CI): скачать ядро, ONNX Runtime, словарь и самый
    /// маленький пакет голосов, синтезировать фразу. Итог — строкой «VVTEST: …» в журнал (logcat) и в файл.
    /// </summary>
    async void DebugVoicevoxTest()
    {
        void Log(string s) { Console.WriteLine("VVTEST: " + s); try { System.IO.File.AppendAllText(System.IO.Path.Combine(ProgressStore.Folder, "vvtest.txt"), s + "\n"); } catch { } }
        Platform.AudioPlayer.Muted = false; // синтез нужен по-настоящему (звук не играем)
        try
        {
            Log($"платформа: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            var smallest = Voicevox.Catalog.OrderBy(p => p.Size).First().File;
            await Voicevox.InstallAsync(new[] { smallest }, new Progress<(double v, string what)>(x => { }), CancellationToken.None);
            Log($"скачано: база {Voicevox.BaseInstalled}, пакеты {string.Join(",", Voicevox.InstalledPacks)}");
            var (ok, msg) = await Voicevox.SelfCheckAsync();
            Log($"{(ok ? "OK" : "FAIL")} {msg}");
        }
        catch (Exception ex) { Log("FAIL " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Замер запуска: ждёт фоновые загрузки, пишет метки времени и память, выходит.</summary>
    async void DebugPerf(string outPath)
    {
        await Task.Delay(500); // первый кадр
        await Perf.WarmedUp;
        System.IO.File.WriteAllText(outPath, Perf.Report());
        if (Win is { } w) w.Close(); else Environment.Exit(0);
    }

    // Рендер собственного окна в PNG (без захвата экрана) и выход — для визуальной проверки
    void DebugSnap(string path)
    {
        DispatcherTimer.RunOnce(() =>
        {
            // KOTODAMA_SNAP_SCALE=1.2 — снимок в повышенном разрешении (для видео)
            double k = double.TryParse(Environment.GetEnvironmentVariable("KOTODAMA_SNAP_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var kk) ? kk : 1;
            var size = new PixelSize((int)(Bounds.Width * k), (int)(Bounds.Height * k));
            using var rtb = new RenderTargetBitmap(size, new Vector(96 * k, 96 * k));
            rtb.Render(this);
            rtb.Save(path);
            if (Win is { } w) w.Close(); else Environment.Exit(0);
        }, TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("KOTODAMA_SNAP_DELAY"), out var sd) ? sd : 2.5));
    }


    /// <summary>Каждый экран открывается и перерисовывается трижды: ловит «already has a visual parent» и прочие ошибки повторной отрисовки.</summary>
    void DebugRerender(string outPath)
    {
        var log = new List<string>();
        void Try(string name, Func<Control> make)
        {
            try
            {
                var page = make();
                Push(page);
                for (int i = 0; i < 3; i++) (page as IPage)?.OnShown();
                log.Add("ok   " + name);
            }
            catch (Exception ex) { log.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
        }
        foreach (var key in Factories.Keys.Where(k => k != "more" || Shell.Compact)) Try(key, () => Factories[key]());
        Try("study", () => { StudyPage.Start(); return (Control)_stack[^1]; });
        Try("pairs", () => new PairsPage());
        Try("speechlesson", () => new SpeechLessonPage(Db.SpeechLessons[0]));
        Try("dialog", () => new DialogPage(Db.Dialogs[0]));
        Try("grammarlesson", () => new GrammarLessonPage(Db.Grammar[0]));
        Try("unit", () => new UnitPage(Db.Course[0]));
        Try("text", () => new TextPage(ReaderPage.Texts[0]));
        Try("word", () => new WordPage(JDict.Lookup("猫")!));
        Try("mic", () => new MicCheckPage());
        Try("radicals", () => new RadicalsPage());
        Try("writing", () => new WritingPage());
        Try("article", () => new ArticlePage(JapanPage.All[0]));
        System.IO.File.WriteAllLines(outPath, log);
        Environment.Exit(0);
    }

    /// <summary>
    /// Обрезанный текст: каждый экран открывается (запускать с --phone и KOTODAMA_PHONE=360x780), и у каждой надписи
    /// проверяется многоточие, выход за свою ширину/высоту и за край экрана. Отчёт — по экранам.
    /// </summary>
    async void DebugClipTest(string outPath)
    {
        var log = new List<string>();
        await Task.Delay(1500);
        var screens = Factories.Keys.Where(k => k != "more" || Shell.Compact).Select(k => ("root:" + k, (Action)(() => NavigateRoot(k)))).ToList();
        foreach (var t in new[] { "learn", "voice", "mic", "look", "notify", "data", "awards", "about" })
            screens.Add(("settings:" + t, () => { SettingsPage._tab = t; NavigateRoot("settings"); if (Shell.Compact) Push(new SettingsPage(t)); }));
        foreach (var o in new[] { "flash", "kanjiflash", "grammar", "grammar1", "onboard", "speech", "dialog", "dialogr", "unit", "reader", "writing", "mic", "pairs", "study", "report",
                                  "radicals", "textl", "word", "article", "match", "build", "listen", "gon", "book", "learn", "learnw", "learn1", "learnk", "learnq", "quiz" })
            screens.Add(("open:" + o, () => { NavigateRoot("home"); DebugOpen(o); }));
        await JDict.LoadAsync();
        foreach (var (name, open) in screens)
        {
            try { open(); } catch (Exception ex) { log.Add($"{name}: FAIL {ex.Message}"); continue; }
            await Task.Delay(900); // анимации появления
            var issues = new List<string>();
            foreach (var tb in this.GetVisualDescendants().OfType<TextBlock>())
            {
                var text = tb.Text ?? tb.Inlines?.Text;
                if (string.IsNullOrWhiteSpace(text) || !tb.IsEffectivelyVisible || tb.Bounds.Width <= 0 || tb.Bounds.Height <= 0) continue;
                var p = tb.TranslatePoint(default, this);
                var lay = tb.TextLayout;
                double w = tb.Bounds.Width - tb.Padding.Left - tb.Padding.Right, h = tb.Bounds.Height - tb.Padding.Top - tb.Padding.Bottom;
                var why = new List<string>();
                if (lay.TextLines.Any(l => l.HasCollapsed)) why.Add("многоточие");
                if (lay.WidthIncludingTrailingWhitespace > w + 8) why.Add($"шире на {lay.WidthIncludingTrailingWhitespace - w:0}");
                if (lay.Height > h + 1.5) why.Add($"выше на {lay.Height - h:0}");
                // сжат соседней колонкой: узкая надпись разбита на много строк («В / каф / е»)
                if (tb.Bounds.Width < Bounds.Width * 0.3 && lay.TextLines.Count >= 3 && text.Contains(' ')) why.Add($"сжат ({lay.TextLines.Count} строк в {tb.Bounds.Width:0} px)");
                // знаки столбиком: надпись без пробелов (японский, короткое слово) сжата до одного-двух знаков в строке
                else if (tb.Bounds.Width < 48 && lay.TextLines.Count >= 3 && text.Trim().Length >= 3) why.Add($"столбиком ({lay.TextLines.Count} строк в {tb.Bounds.Width:0} px)");
                // перенос посреди слова: строка кончается буквой и следующая начинается буквой («Аудировани / е»)
                for (int li = 0; li + 1 < lay.TextLines.Count && !text.Contains('\\') && !text.Contains("://"); li++) // пути и ссылки переносить можно
                {
                    int end = lay.TextLines[li].FirstTextSourceIndex + lay.TextLines[li].Length;
                    if (end > 0 && end < text.Length && char.IsLetter(text[end - 1]) && char.IsLetter(text[end]) && text[end - 1] < 0x3000 && text[end] < 0x3000)
                    { why.Add($"разрыв слова «…{text[Math.Max(0, end - 6)..end]}|{text[end..Math.Min(text.Length, end + 4)]}…»"); break; }
                }
                bool inHScroll = tb.GetVisualAncestors().OfType<ScrollViewer>().Any(s => s.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled);
                if (p != null && !inHScroll && (p.Value.X + tb.Bounds.Width > Bounds.Width + 1.5 || p.Value.X < -1.5)) why.Add($"за краем экрана (x={p.Value.X:0}, w={tb.Bounds.Width:0})");
                if (why.Count > 0)
                    issues.Add($"    [{string.Join(", ", why)}] «{(text.Length > 60 ? text[..60] + "…" : text).Replace('\n', ' ')}»");
            }
            // наложение: две надписи занимают одно место (декоративные полупрозрачные иероглифы-водяные знаки не в счёт)
            var boxes = new List<(Rect r, string t)>();
            foreach (var tb in this.GetVisualDescendants().OfType<TextBlock>())
            {
                var text = tb.Text ?? tb.Inlines?.Text;
                if (string.IsNullOrWhiteSpace(text) || !tb.IsEffectivelyVisible || tb.Bounds.Width <= 0) continue;
                double op = 1; for (Visual? v = tb; v != null; v = v.GetVisualParent()) op *= v.Opacity;
                if (op < 0.5) continue;
                var p = tb.TranslatePoint(default, this);
                if (p == null) continue;
                var lay = tb.TextLayout;
                double w = Math.Min(tb.Bounds.Width, lay.WidthIncludingTrailingWhitespace), h = Math.Min(tb.Bounds.Height, lay.Height);
                double x = p.Value.X + tb.TextAlignment switch { TextAlignment.Center => (tb.Bounds.Width - w) / 2, TextAlignment.Right => tb.Bounds.Width - w, _ => 0 };
                var r = new Rect(x, p.Value.Y, w, h);
                // что прокручено за край своей области — обрезано и не видно: считаем только видимую часть
                foreach (var sv in tb.GetVisualAncestors().OfType<ScrollViewer>())
                    if (sv.TranslatePoint(default, this) is { } sp) r = r.Intersect(new Rect(sp, sv.Bounds.Size));
                if (r.Width > 1 && r.Height > 1) boxes.Add((r, text));
            }
            for (int a = 0; a < boxes.Count; a++)
                for (int b = a + 1; b < boxes.Count; b++)
                {
                    var i = boxes[a].r.Intersect(boxes[b].r);
                    double small = Math.Min(boxes[a].r.Width * boxes[a].r.Height, boxes[b].r.Width * boxes[b].r.Height);
                    if (i.Width > 2 && i.Height > 2 && i.Width * i.Height > small * 0.25)
                        issues.Add($"    [наложение] «{Short(boxes[a].t)}» и «{Short(boxes[b].t)}»");
                }
            static string Short(string s) => (s.Length > 30 ? s[..30] + "…" : s).Replace('\n', ' ');
            log.Add($"{name}: {(issues.Count == 0 ? "ok" : issues.Count + " проблем")}");
            // снимок экрана с проблемами (KOTODAMA_CLIPSNAP=папка) — чтобы глазами отличить настоящую обрезку
            if ((issues.Count > 0 || Environment.GetEnvironmentVariable("KOTODAMA_CLIPSNAP_ALL") == "1") && Environment.GetEnvironmentVariable("KOTODAMA_CLIPSNAP") is { Length: > 0 } dir)
            {
                using var rtb = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                rtb.Render(this);
                rtb.Save(System.IO.Path.Combine(dir, name.Replace(':', '_') + ".png"));
            }
            log.AddRange(issues.Distinct());
            System.IO.File.WriteAllLines(outPath, log);
        }
        if (Win is { } w2) w2.Close(); else Environment.Exit(0);
    }

    /// <summary>Шумоподавление: фразы с наложенным шумом (вентилятор + гул + шипение, SNR ≈ 8 дБ) — балл без фильтра и с ним.</summary>
    async void DebugNoiseBench(string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        await JDict.LoadAsync();
        var rnd = new Random(7);
        var items = Db.SpeechLessons.SelectMany(l => l.Items.Select(i => (i.Jp, i.KanaPlain))).Distinct().Where((_, k) => k % 4 == 0).Take(22).ToList();
        int n = 0; long sClean = 0, sOff = 0, sOn = 0; int passOff = 0, passOn = 0;
        foreach (var (jp, kana) in items)
        {
            var (x, rate) = Platform.Pcm.Decode((await Tts.GetAsync(jp))!);
            var clean = Platform.Pcm.Resample(x, rate, 16000);
            double rms = Math.Sqrt(clean.Average(v => (double)v * v));
            double nAmp = rms / Math.Pow(10, (double.TryParse(Environment.GetEnvironmentVariable("KOTODAMA_SNR"), out var snr) ? snr : 8) / 20.0);
            // запас тишины до и после — как в настоящей записи
            var y = new float[clean.Length + 16000];
            Array.Copy(clean, 0, y, 8000, clean.Length);
            double lp = 0;
            for (int i = 0; i < y.Length; i++)
            {
                double w = rnd.NextDouble() * 2 - 1;
                lp = lp * 0.97 + w * 0.03;                       // «вентилятор» — низкочастотный шум
                double v = lp * 6 + w * 0.35 + 0.3 * Math.Sin(2 * Math.PI * 50 * i / 16000.0);
                y[i] += (float)(v * nAmp);
            }
            var wavClean = Recorder.ToWav(Platform.Pcm.ToPcm16(clean));
            var wav = Recorder.ToWav(Platform.Pcm.ToPcm16(y));
            Recording R(byte[] w) => new(w, true, Array.Empty<float>(), w.Length / 32000.0, 0.5f, 0.01f);
            Asr.UseDenoise = false;
            var c0 = await Views.SpeechUi.ScoreRecording(jp, kana, R(wavClean), CancellationToken.None);
            var a = await Views.SpeechUi.ScoreRecording(jp, kana, R(wav), CancellationToken.None);
            Asr.UseDenoise = true;
            var b = await Views.SpeechUi.ScoreRecording(jp, kana, R(wav), CancellationToken.None);
            n++; sClean += c0.Score; sOff += a.Score; sOn += b.Score;
            if (a.Score >= 70) passOff++;
            if (b.Score >= 70) passOn++;
            L($"{c0.Score,3} {a.Score,3} -> {b.Score,3}  {jp} | off '{a.Heard}' on '{b.Heard}'");
        }
        L($"n={n} clean={sClean / Math.Max(1, n)} noisy-off={sOff / Math.Max(1, n)} noisy-on={sOn / Math.Max(1, n)} pass70 off={passOff} on={passOn}");
        Environment.Exit(0);
    }

    /// <summary>Сравнение моделей распознавания на фразах уроков (озвучка из кэша): время и баллы обоих проходов.</summary>
    async void DebugAsrBench(string model, string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        Asr.OverridePath = model;
        await JDict.LoadAsync();
        var items = Db.SpeechLessons.SelectMany(l => l.Items.Select(i => (i.Jp, i.KanaPlain))).Distinct().ToList();
        var sw = new System.Diagnostics.Stopwatch();
        long t1 = 0, tAll = 0; int n = 0, fails = 0, second = 0; long sum = 0;
        foreach (var (jp, kana) in items)
        {
            var mp3 = await Tts.GetAsync(jp);
            if (mp3 == null) continue;
            var (x, rate) = Platform.Pcm.Decode(mp3);
            var pcm = Platform.Pcm.ToPcm16(Platform.Pcm.Resample(x, rate, 16000));
            var rec = new Recording(Recorder.ToWav(pcm), true, Array.Empty<float>(), pcm.Length / 32000.0, 0.5f, 0.001f);
            sw.Restart();
            var r1 = await Asr.TranscribeDetailedAsync(rec.Wav, CancellationToken.None);
            t1 += sw.ElapsedMilliseconds;
            var s1 = Speech.Score(r1.Text, r1.Confidence, jp, kana);
            sw.Restart();
            var sc = await Views.SpeechUi.ScoreRecording(jp, kana, rec, CancellationToken.None);
            tAll += sw.ElapsedMilliseconds;
            if (s1.Score < 88) second++;
            n++; sum += sc.Score;
            if (sc.Score < 70) fails++;
            if (s1.Score < 80) L($"  {s1.Score,3}->{sc.Score,3} {jp} | heard '{r1.Text}' low: {string.Join(" ", r1.Tokens.Where(t => t.p < 0.5).Select(t => $"{t.text}:{t.p:0.00}"))}");
        }
        L($"MODEL {System.IO.Path.GetFileName(model)}: n={n} avg={sum / Math.Max(1, n)} fails={fails} pass1={t1 / Math.Max(1, n)}ms full={tAll / Math.Max(1, n)}ms second-pass={second}");
        Environment.Exit(0);
    }

#if !MOBILE
    /// <summary>Беззвучная проверка воспроизведения: озвучка обычная и медленная, своя запись, звуки интерфейса.</summary>
    async void DebugPlay(string outPath)
    {
        var log = new List<string>();
        void L(string x) { log.Add(x); System.IO.File.WriteAllLines(outPath, log); }
        Platform.AudioPlayer.Muted = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        L($"say normal: {await Speech.SayAsync("こんにちは")} {sw.ElapsedMilliseconds} ms"); sw.Restart();
        L($"say slow: {await Speech.SayAsync("こんにちは", true)} {sw.ElapsedMilliseconds} ms"); sw.Restart();
        var wav = Recorder.ToWav(Platform.Pcm.ToPcm16(Enumerable.Range(0, 8000).Select(i => (float)Math.Sin(i * 0.1) * 0.2f).ToArray()));
        L($"play wav: {await Speech.PlayWavAsync(wav)} {sw.ElapsedMilliseconds} ms");
        L($"mic available: {Recorder.HasMicrophone} devices: {string.Join(", ", Recorder.Devices())}");
        Environment.Exit(0);
    }

#endif

    void DebugOpen(string what)
    {
        switch (what)
        {
            case "flash": Push(new Controls.FlashSession("Демо", Db.Words.Take(5).Select(Controls.FlashSession.FromWord))); break;
            case "kanjiflash": Push(new Controls.FlashSession("Демо", Db.Kanji.Take(5).Select(Controls.FlashSession.FromKanji))); break;
            case "grammar": Push(new GrammarLessonPage(Db.Grammar[4])); break;
            case "grammar1": Push(new GrammarLessonPage(Db.Grammar[0])); break;
            case "grammarn2": Push(new GrammarLessonPage(Db.Grammar[^4])); break;
            case "onboard": Push(new OnboardingPage()); break;
            case "speech": Push(new SpeechLessonPage(Db.SpeechLessons[1])); break;
            case "dialog": Push(new DialogPage(Db.Dialogs[1])); break;
            case "dialogr": { var dp = new DialogPage(Db.Dialogs[1]); dp.RecallPreview = true; Push(dp); break; }
            case "unit": Push(new UnitPage(Db.Course[0])); break;
            case "reader": Push(new ReaderPage()); break;
            case "writing": Push(new WritingPage()); break;
            case "mic": Push(new MicCheckPage()); break;
            case "pairs": Push(new PairsPage()); break;
            case "study": StudyPage.Start(); break;
            case "art": Push(new ArtDemo()); break;
            case "report": Push(new UserControl { Content = U.Page(Report.Build()) }); break;
            case "radicals": Push(new RadicalsPage()); break;
            case "textl": Push(new TextPage(ReaderPage.Texts.First(t => t.Book != null), true)); break;
            case "word": JDict.LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() => Push(new WordPage(JDict.Lookup("勉強")!)))); break;
            case "wordv": JDict.LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() => Push(new WordPage(JDict.Lookup("食べる")!)))); break;
            case "worda": JDict.LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() => Push(new WordPage(JDict.Lookup("高い")!)))); break;
            case "article": Push(new ArticlePage(JapanPage.All[0])); break;
            case "match": Push(new MatchRushGame()); break;
            case "player": Push(new PlayerPage()); break;
            case "keys": ShowHotkeys(); break;
            case "rail": ProgressStore.P.SidebarRail = true; ApplyRail(false); break;
            case "typing": Push(new TypingPage()); break;
            case "numbers": Push(new NumbersPage()); break;
            case "numq": Push(NumbersPage.ReadNumbers()); break;
            case "numl": Push(NumbersPage.Prices()); break;
            case "build": Push(new WordBuilderGame()); break;
            case "listen": Push(new ListenCatchGame()); break;
            case "gon": Push(new TextPage(ReaderPage.Texts.First(t => t.Book == "Лисёнок Гон"))); break;
            case "book": Push(new BookPage("Японские сказки", ReaderPage.Texts.Where(t => t.Book == "Японские сказки").ToArray())); break;
            case "setvoice": SettingsPage._tab = "voice"; NavigateRoot("settings"); if (Shell.Compact) Push(new SettingsPage("voice")); break;
            case "learn": Push(Controls.LearnSession.Kanji(Db.Kanji.Take(5))); break;
            case "learnw": Push(Controls.LearnSession.Words("Демо", Db.Words.Take(5)).Skip(1)); break;
            case "learn1": Push(Controls.LearnSession.Kanji(Db.Kanji.Skip(20).Take(5)).Skip(2)); break;
            case "learnk": Push(new Controls.LearnSession("Хирагана", Kana.All.Take(10).Select(c => Controls.LearnItem.From(c, false))).Skip(2)); break;
            case "learnq": Push(Controls.LearnSession.Words("Демо", Db.Words.Take(5)).Skip(7)); break;
#if !MOBILE
#endif
            case "wod": WordOfDay.Show(); break;
            case "week": WeekSummary.ShowLatest(); break;
            case "quiz":
            case "quizwrong":
                var w = Db.Words.Where(x => x.Jp.Any(Reading.IsKanji)).Take(8).ToArray();
                var qs = new Controls.QuizSession("Демо", w.Select(x => new Controls.QuizQ { Prompt = x.Jp, Sub = x.Kana, Answer = x.Ru, Key = x.Id, Options = Controls.QuizSession.MakeOptions(x.Ru, w.Select(y => y.Ru)) }));
                Push(qs);
                if (what == "quizwrong") DispatcherTimer.RunOnce(qs.DebugWrong, TimeSpan.FromSeconds(1));
                break;
        }
    }
}

/// <summary>Витрина собственной графики (только отладка).</summary>
public sealed class ArtDemo : UserControl, IPage
{
    public string Title => "Графика";
    public void OnShown()
    {
        var root = new StackPanel();
        var row = new WrapPanel();
        foreach (Controls.Mood m in Enum.GetValues<Controls.Mood>()) row.Children.Add(Controls.Art.Mascot(220, m).M(0, 0, 16, 16));
        root.Children.Add(row);
        var row2 = new WrapPanel();
        foreach (var k in new[] { "student", "cheer", "teacher", "casual", "bunny", "astronaut", "hello", "reading", "head", "classic" }) row2.Children.Add(Controls.Art.Anime(k, 160).M(0, 0, 16, 16));
        root.Children.Add(row2);
        var icons = new WrapPanel();
        foreach (var n in new[] { "home","path","kana","words","kanji","grammar","speech","dialogs","reader","books","dict","review","trainers","settings","more","phrases","voice","mic","palette","bell","target","shield","download","trophy","info","flame","games","write","storage","sparkle","medal","crown","gift","scroll","fox","brush","haiku","moon","flag","star","chart","leaf" })
            icons.Children.Add(Controls.Art.Icon(n, 48).M(0, 0, 12, 12));
        root.Children.Add(icons);
        root.Children.Add(Controls.Art.Say("Привет! Я Википе-тан. Давай учить японский вместе!", Controls.Mood.Joy));
        Content = U.Page(root);
    }
}
#endif
