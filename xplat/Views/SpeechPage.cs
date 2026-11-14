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

public sealed class SpeechPage : UserControl, IPage
{
    public string Title => "Говорение";

    public void OnShown()
    {
        var root = new StackPanel();
        root.Children.Add(U.Header("話", "Говорение", "Слушай носителя, повторяй вслух — Whisper распознает речь и оценит произношение."));
        root.Children.Add(SpeechUi.ModelCard(OnShown));
        var mic = U.Btn("Проверка и настройка микрофона", () => Shell.Open(new MicCheckPage()), "", "\uE720");
        mic.HorizontalAlignment = HorizontalAlignment.Left; mic.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(mic);

        root.Children.Add(U.T("Уроки", "H2").M(0, 26, 0, 12));
        // телефон: колонок столько, сколько влезает (на узком экране 320 — одна, иначе названия рвались посреди слова)
        Avalonia.Controls.Primitives.UniformGrid grid = Shell.Compact ? new TileGrid { MinTile = 160, MaxColumns = 2, Margin = new Thickness(-6, 0, -6, 0) } : new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 0) };
        grid.Children.Add(PersonalCard());
        grid.Children.Add(PairsCard());
        foreach (var l in Db.SpeechLessons.Take(6)) grid.Children.Add(LessonCard(l));
        root.Children.Add(grid);
        Anim.Stagger(grid, 35, 80);
        U.Deferred(grid, Db.SpeechLessons.Skip(6).Select(l => (Func<Control>)(() => LessonCard(l))));
        Content = U.Page(root);
    }

    /// <summary>
    /// «Мои слова вслух»: 10 слов из своих карточек. Сначала те, что ещё ни разу не удалось сказать
    /// на 70+, затем ближайшие к повторению. Если карточек мало — слова текущего уровня.
    /// </summary>
    public static SpeechLesson PersonalLesson()
    {
        var p = ProgressStore.P;
        var byId = Db.Words.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
        var mine = p.Cards.Where(kv => byId.ContainsKey(kv.Key)).OrderBy(kv => kv.Value.Due).Select(kv => byId[kv.Key]).ToList();
        if (mine.Count < 10) mine.AddRange(Db.WordsOf(p.Level).Where(w => !mine.Contains(w)).Take(10 - mine.Count));
        int Best(Word w) => p.SpeechBest.TryGetValue("sw:" + w.Id, out var s) ? s : -1;
        var pick = mine.OrderBy(w => Best(w) >= Speech.PassScore ? 1 : 0).ThenBy(_ => Random.Shared.Next()).Take(10).ToArray();
        var items = pick.Select(w => new Phrase("sw:" + w.Id, w.Jp, w.Kana, w.Ru, w.ExJp.Length > 0 ? $"В речи: {w.ExJp} — {w.ExRu}" : null)).ToArray();
        return new SpeechLesson(900, "Мои слова вслух", "声", "10 слов из твоих карточек", items);
    }

    static Border PersonalCard()
    {
        int cards = ProgressStore.P.Cards.Count;
        var sp = new StackPanel();
        sp.Children.Add(Art.Icon("mic", 44).Also(e => e.HorizontalAlignment = HorizontalAlignment.Left));
        sp.Children.Add(U.T("Мои слова вслух", "H3", 15).M(0, 12, 0, 3));
        sp.Children.Add(U.T(cards > 0 ? "10 слов из твоих карточек — каждый раз новые" : "10 слов твоего уровня", "Muted").Also(t => t.MinHeight = 36));
        sp.Children.Add(U.Pill("Особое", U.B("AccentSoft"), U.B("Accent")).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); }));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new SpeechLessonPage(PersonalLesson()));
        return card;
    }

    static Border PairsCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(Art.Icon("voice", 44).Also(t => t.HorizontalAlignment = HorizontalAlignment.Left));
        sp.Children.Add(U.T("Минимальные пары", "H3", 15).M(0, 12, 0, 3));
        sp.Children.Add(U.T("おばさん или おばあさん? Долгота, っ, звонкость", "Muted").Also(t => t.MinHeight = 36));
        sp.Children.Add(U.Pill("Особое", U.B("AccentSoft"), U.B("Accent")).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 10, 0, 0); }));
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new PairsPage());
        return card;
    }

    static Border LessonCard(SpeechLesson l)
    {
        int done = l.Items.Count(i => ProgressStore.P.SpeechBest.TryGetValue(i.Id, out var s) && s >= Speech.PassScore);
        double avg = l.Items.Average(i => ProgressStore.P.SpeechBest.TryGetValue(i.Id, out var s) ? s : 0);
        var sp = new StackPanel();
        var head = U.Cols("auto", "*", "auto");
        head.Children.Add(Emoji.Img(Emoji.For(l.Title, "speaking_head"), 40));
        head.Children.Add(U.T($"{done}/{l.Items.Length}", "Muted", 12).CenterV().Col(2));
        sp.Children.Add(head);
        sp.Children.Add(U.T(l.Title, "H3", 15).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 12, 0, 3));
        sp.Children.Add(U.T(l.Desc, "Muted").Also(t => t.MinHeight = 36));
        sp.Children.Add(new ProgressBar { Maximum = 100, Value = avg, Height = 4, Margin = new Thickness(0, 12, 0, 0) });
        var card = new Border { Classes = { "Card" }, Child = sp, Margin = new Thickness(6), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new SpeechLessonPage(l));
        return card;
    }
}

/// <summary>Урок говорения: образец → запись → распознавание → оценка, волны «образец / ты».</summary>
public sealed class SpeechLessonPage : UserControl, IPage, IKeyTarget, IResumable, IFocusPage
{
    readonly SpeechLesson _l;
    public string ResumeKey => $"speech:{_l.Id}";
    public string ResumeTitle => $"Говорение · {_l.Title}";
    int _i;
    CancellationTokenSource? _cts;
    Recorder? _rec;
    bool _busy;
    Recording? _last;
    public string Title => _l.Title;
    Phrase P => _l.Items[_i];

    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly ContentControl _phrase = new();
    readonly MicButton _mic = new();
    readonly SpeechUi.LevelMeter _meter = new();
    readonly TextBlock _status = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    readonly ContentControl _result = new();
    readonly ContentControl _kanaHost = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Grid _wrap = new();

    public SpeechLessonPage(SpeechLesson l)
    {
        _l = l;
        _status.Foreground = U.B("Muted");
        _mic.Click += ToggleRecord;

        var root = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(_dots);
        root.Children.Add(_phrase.M(0, 16, 0, 0));
        root.Children.Add(_mic.M(0, 0, 0, -12));
        root.Children.Add(_meter);
        var shadow = U.Btn("Шэдоуинг: говори вместе с носителем", Shadow, "Ghost", "\uE7F5");
        shadow.HorizontalAlignment = HorizontalAlignment.Center; ToolTip.SetTip(shadow, "Лучше в наушниках: запись начнётся одновременно с образцом");
        root.Children.Add(shadow);
        root.Children.Add(_status);
        root.Children.Add(_result.M(0, 16, 0, 0));

        var nav = U.Cols("auto", "*", "auto");
        nav.Margin = new Thickness(0, 20, 0, 0);
        nav.Children.Add(U.Btn("Назад", () => Go(-1), "Ghost", ""));
        nav.Children.Add(U.T("Пробел — говорить · L — слушать · S — медленно", "Muted", 12).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis).DesktopOnly().Center().CenterV().M(16, 0, 16, 0).Col(1));
        nav.Children.Add(U.Btn("Дальше", () => Go(1), "", "").Col(2));
        root.Children.Add(nav);

        _wrap.Children.Add(root);
        Content = U.Page(_wrap);
        Unloaded += (_, _) => { _cts?.Cancel(); _rec?.Stop(); };
        Asr.Warmup();
        Render();
    }

    void Render()
    {
        RenderDots();
        var p = P;
        var sp = new StackPanel();
        var top = U.Cols("*", "auto");
        top.Children.Add(U.T($"Фраза {_i + 1} из {_l.Items.Length}", "Muted", 12).CenterV());
        top.Children.Add(U.Row(4, U.SpeakBtn(() => p.Jp, true), U.SpeakBtn(() => p.Jp)).Col(1));
        sp.Children.Add(top);
        // телефон: короткая фраза — в одну строку (кегль по длине), иначе последний слог уезжал на новую строку
        double size = Shell.Compact ? Math.Clamp(250.0 / Math.Max(1, p.Jp.Length), 22, 40) : p.Jp.Length > 12 ? 32 : 40;
        sp.Children.Add(U.Jp(p.Jp, size, null, true).Center().M(0, 6, 0, 0));
        // строка каны; после попытки здесь же подсвечиваются слоги с ошибками (нажатие — послушать только слог)
        _kanaHost.Content = p.KanaPlain != p.Jp ? U.Jp(p.KanaPlain, 17, U.B("Accent")).Center() : null;
        sp.Children.Add(_kanaHost.Detach().M(0, 6, 0, 0));
        sp.Children.Add(U.T(p.Romaji, "Body", 15, U.B("Muted")).Center().M(0, 2, 0, 0));
        sp.Children.Add(U.T(p.Ru, "H3", 17).Center().M(0, 12, 0, 0));
        if (p.Tip != null)
            sp.Children.Add(new Border
            {
                Background = U.B("Surface2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Child = U.Row(10, new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Center }, U.T(p.Tip, "Body", 13).Also(t => t.MaxWidth = 620)),
            });
        var card = U.Card(sp, new Thickness(26, 18, 26, 22));
        _phrase.Content = card;
        if (Tts.UseOffline) // VOICEVOX уже работает для озвучки — заодно берём из него мелодику фразы
        {
            var jp = p.Jp;
            Task.Run(() => Voicevox.Accent(jp)).ContinueWith(t =>
            {
                if (t.Result is { Count: > 0 } moras && P.Jp == jp)
                {
                    var av = new AccentView(moras) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0)}.Tip("Питч-акцент: линия показывает высокий и низкий тон каждой моры");
                    sp.Children.Insert(2, av);
                    Anim.FadeUp(av, 0, 6, 300);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        Anim.FadeUp(card, 0, 10, 300);

        int best = ProgressStore.P.SpeechBest.TryGetValue(p.Id, out var b0) ? b0 : -1;
        _status.Text = !Asr.Ready ? "Скачай модель распознавания в разделе «Говорение», чтобы проверять произношение. Слушать можно уже сейчас."
                     : best >= 0 ? $"Лучший результат: {best}. Нажми на микрофон и повтори" : "Послушай образец, затем нажми на микрофон и повтори";
        _result.Content = null;
        _last = null;
        if (ProgressStore.AutoSpeakOn) Speech.Say(p.Jp);
        _ = Tts.GetAsync(p.Jp, true); // заранее готовим медленный вариант
        if (_i + 1 < _l.Items.Length) _ = Tts.GetAsync(_l.Items[_i + 1].Jp); // и следующую фразу — «Дальше» звучит без задержки
    }

    void RenderDots()
    {
        _dots.Children.Clear();
        for (int i = 0; i < _l.Items.Length; i++)
        {
            int best = ProgressStore.P.SpeechBest.TryGetValue(_l.Items[i].Id, out var s) ? s : -1;
            _dots.Children.Add(new Border
            {
                Width = i == _i ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(3),
                Background = i == _i ? U.B("Accent") : best >= Speech.PassScore ? U.B("Good") : best >= 0 ? U.B("Gold") : U.B("Surface3"),
            });
        }
    }

    void Go(int d)
    {
        _cts?.Cancel();
        int n = _i + d;
        if (n < 0) return;
        if (n >= _l.Items.Length)
        {
            // Урок засчитывается, когда сказано на зачёт не меньше 80% фраз — пролистать недостаточно
            int ok = _l.Items.Count(it => ProgressStore.P.SpeechBest.TryGetValue(it.Id, out var b) && b >= Speech.PassScore);
            int need = (int)Math.Ceiling(_l.Items.Length * 0.8);
            if (ok >= need)
            {
                ProgressStore.P.Flags.Add("speech:" + _l.Id);
                ProgressStore.Notify();
                Shell.Toast("Урок пройден! お疲れ様でした", "話");
            }
            else Shell.Toast($"Засчитано фраз: {ok} из {_l.Items.Length}. Урок будет пройден, когда получится {need}.", "話");
            Shell.Back();
            return;
        }
        _i = n;
        Render();
    }

    async void ToggleRecord()
    {
        if (_busy) { _rec?.Stop(); return; }
        if (!Asr.Ready) { Shell.Toast("Сначала скачай модель распознавания (раздел «Говорение»)", "!"); return; }
        _busy = true;
        _cts = new CancellationTokenSource();
        _rec = new Recorder();
        SetMic(true);
        _status.Text = "Включаю микрофон…";
        string listen = ProgressStore.P.MicMode == "manual" ? (Shell.Compact ? "Говори! Затем нажми на микрофон, чтобы закончить" : "Говори! Затем нажми на микрофон (или пробел), чтобы закончить") : "Говори! Запись остановится сама после паузы";
        _rec.Started += () => Dispatcher.UIThread.Post(() => { if (_busy) _status.Text = listen; });
        _result.Content = null;
        var p = P;
        try
        {
            var (check, rec) = await SpeechUi.RecordAndScore(p.Jp, p.KanaPlain, _rec, _cts.Token, s => { _status.Text = s; _mic.Set(MicButton.Mode.Thinking); _meter.Attach(null); });
            _last = rec;
            ProsodyResult? pr = null;
            int textScore = check.Score;
            if (check.Error == null && rec is { Heard: true } && check.Score >= 40 && !Speech.Easy)
            {
                _status.Text = "Анализирую интонацию…";
                pr = await SpeechUi.AnalyzeProsody(p.Jp, rec);
            }
            if (pr != null) check = check.WithScore(SpeechUi.Combine(check.Score, pr));
            ShowResult(p, check, rec, pr, textScore);
        }
        catch (OperationCanceledException) { _status.Text = "Остановлено"; }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
        finally
        {
            _busy = false;
            SetMic(false);
            _rec?.Dispose(); _rec = null;
        }
    }

    /// <summary>В наушниках ли ученик (спрашиваем один раз за запуск): без них микрофон пишет образец из динамиков.</summary>
    static bool? _headphones;

    /// <summary>
    /// Шэдоуинг. В наушниках — образец звучит, и одновременно идёт запись. Без наушников — сначала образец,
    /// потом запись (иначе оценивался бы голос синтезатора). Запись, совпадающая с образцом по форме, не засчитывается.
    /// </summary>
    async void Shadow()
    {
        if (_busy || !Asr.Ready) { if (!Asr.Ready) Shell.Toast("Сначала скачай модель распознавания", "!"); return; }
        _headphones ??= await Shell.Confirm("Шэдоуинг — говорить одновременно с носителем — работает в наушниках: иначе микрофон запишет голос из динамиков. Без наушников запись начнётся сразу после образца.", "Я в наушниках", "Без наушников");
        var p = P;
        var path = await Tts.GetAsync(p.Jp);
        if (path == null) return;
        var sample = await Task.Run(() => Prosody.DecodeFile(path));
        double dur = sample.Length / 16000.0;
        _busy = true;
        _cts = new CancellationTokenSource();
        bool together = _headphones == true;
        _rec = new Recorder(manual: true, maxSeconds: dur + 2.5);
        try
        {
            if (!together)
            {
                _status.Text = "Слушай образец…";
                await Speech.SayAsync(p.Jp);
            }
            SetMic(true);
            _status.Text = together ? "Говори вместе с голосом!" : "Теперь повтори так же!";
            var task = _rec.RecordAsync(_cts.Token);
            await Task.Delay(120);
            if (together) _ = Speech.SayAsync(p.Jp);
            await Task.Delay(TimeSpan.FromSeconds(dur + 1.0));
            _rec.Stop();
            var rec = await task;
            _last = rec;
            if (together && await Task.Run(() => Echo.Similarity(Prosody.FromWav(rec.Wav), sample)) >= Echo.EchoThreshold)
            {
                _headphones = null; // спросим ещё раз
                _status.Text = "";
                _result.Content = U.Card(U.T("Похоже, микрофон записал голос из динамиков, а не тебя. Надень наушники или выбери «Без наушников» — тогда запись начнётся после образца.", "Body").Center());
                return;
            }
            _status.Text = "Распознаю…"; _mic.Set(MicButton.Mode.Thinking); _meter.Attach(null);
            var check = await SpeechUi.ScoreRecording(p.Jp, p.KanaPlain, rec, _cts.Token, s => { _status.Text = s; _mic.Set(MicButton.Mode.Thinking); _meter.Attach(null); });
            int textScore = check.Score;
            var pr = rec.Heard ? await SpeechUi.AnalyzeProsody(p.Jp, rec) : null;
            if (pr != null) check = check.WithScore(SpeechUi.Combine(check.Score, pr));
            ShowResult(p, check, rec, pr, textScore);
        }
        catch (OperationCanceledException) { _status.Text = "Остановлено"; }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
        finally { _busy = false; SetMic(false); _rec?.Dispose(); _rec = null; }
    }

    void SetMic(bool on)
    {
        _mic.Set(on ? MicButton.Mode.Recording : MicButton.Mode.Idle, _rec);
        _meter.Attach(on ? _rec : null);
    }

    void ShowResult(Phrase p, SpeechCheck r, Recording? rec, ProsodyResult? pr = null, int textScore = 0)
    {
        if (r.Error != null)
        {
            _status.Text = "";
            var err = U.Stack(U.T(r.Error, "Body").Center(), U.Btn("Доступ к микрофону", Speech.OpenMicPrivacy, "", "").Center().M(0, 12, 0, 0).Also(b => b.IsVisible = OperatingSystem.IsWindows()));
            var ec = U.Card(err); ec.BorderBrush = U.B("Bad");
            _result.Content = ec;
            return;
        }
        if (r.Heard.Length > 0) ProgressStore.SpeechResult(p.Id, r.Score);
        if (r.HeardKana.Length > 0)
        {
            var moras = SpeechDetail.PhraseMoras(p.Jp, p.KanaPlain, r.HeardKana, r.Tokens, 20);
            _kanaHost.Content = moras;
            Anim.FadeUp(moras, 0, 6, 260);
        }
        _status.Text = "";
        var sp = new StackPanel();

        var top = U.Cols("auto", "*", "auto");
        if (Speech.Easy) top.Children.Add(Art.Chibi(78, r.Ok ? (r.Score >= 85 ? Mood.Joy : Mood.Happy) : Mood.Think).CenterV().Col(2));
        var ring = new Grid { Width = 92, Height = 92 };
        ring.Children.Add(new Ellipse { Stroke = U.B("Surface3"), StrokeThickness = 7 });
        var arc = Results.Ring(46, 42.5, r.Score / 100.0);
        arc.Stroke = U.B(r.Ok ? "Good" : r.Score >= 35 ? "Gold" : "Bad");
        arc.StrokeThickness = 7; arc.StrokeLineCap = PenLineCap.Round;
        ring.Children.Add(arc);
        var num = U.T("0", "H1", 26);
        ring.Children.Add(num.Center().CenterV());
        Anim.CountUp(num, r.Score);
        top.Children.Add(ring);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        if (r.Ok) info.Children.Add(U.Pill("✓ ЗАСЧИТАНО", U.B("GoodSoft"), U.B("Good")).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 6); }));
        info.Children.Add(U.T(r.Verdict, "H3", 15, U.B(r.Ok ? "Good" : r.Score >= 35 ? "Gold" : "Bad")));
        if (r.Heard.Length > 0)
        {
            info.Children.Add(U.T("РАСПОЗНАНО", "Overline").M(0, 10, 0, 3));
            info.Children.Add(r.HeardKana.Length > 0 ? SpeechDetail.MoraChips(p.Jp, p.KanaPlain, r.HeardKana, r.Tokens) : Diff(p.Jp, p.KanaPlain, r.Heard, r.HeardKana));
            if (r.HeardKana.Length > 0 && Speech.Normalize(r.Heard) != Speech.Normalize(r.HeardKana))
                info.Children.Add(U.T($"Whisper записал: {r.Heard}", "Muted", 12).M(0, 2, 0, 0));
        }
        top.Children.Add(info.Col(1));
        sp.Children.Add(top);
        if (SpeechUi.HintsPanel(r) is { } hints) sp.Children.Add(hints);
        if (r.Ok && !Speech.Easy && SpeechDetail.UnclearCount(r.Tokens) > 0)
            sp.Children.Add(U.T("Засчитано, но часть мор прозвучала нечётко (оранжевая рамка) — открывай рот чуть шире и не глотай окончания.", "Body", 13, U.B("Gold")).M(0, 10, 0, 0));
        // Лёгкий режим: подробный разбор (питч-акцент, интонация, волны) — по кнопке, чтобы не перегружать
        var det = sp;
        if (Speech.Easy && rec is { Heard: true })
        {
            det = new StackPanel();
            var more = U.Btn("Подробный разбор: интонация и волны", () => { }, "Ghost", "");
            more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 10, 0, 0);
            var detail = det;
            more.Click += async (_, _) =>
            {
                more.IsVisible = false;
                var pros = await SpeechUi.AnalyzeProsody(p.Jp, rec);
                if (pros != null)
                {
                    detail.Children.Insert(Math.Min(2, detail.Children.Count), new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(0, 16, 0, 12) });
                    detail.Children.Insert(Math.Min(3, detail.Children.Count), SpeechUi.ProsodyPanel(textScore, pros));
                }
                detail.IsVisible = true;
                Anim.FadeUp(detail, 0, 8, 260);
            };
            det.IsVisible = false;
            sp.Children.Add(more);
            sp.Children.Add(det);
        }
        if (rec != null && rec.Heard && SpeechDetail.AccentPanel(p.Jp, p.KanaPlain, rec.Wav) is { } accent)
        {
            det.Children.Add(new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(0, 16, 0, 12) });
            det.Children.Add(accent);
        }
        if (pr != null)
        {
            det.Children.Add(new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(0, 16, 0, 12) });
            det.Children.Add(SpeechUi.ProsodyPanel(textScore, pr));
        }

        // Волны «образец / ты»: видно длину и ритм — долгие гласные и паузы っ
        if (rec != null && rec.Heard)
        {
            var sp0 = sp; sp = det;
            sp.Children.Add(new Border { Height = 1, Background = U.B("Stroke"), Margin = new Thickness(0, 16, 0, 12) });
            var sample = new WaveView(U.B("Accent"));
            var mine = new WaveView(U.B("Info"));
            mine.SetPeaks(rec.Peaks, rec.Seconds);
            sp.Children.Add(WaveRow("Образец", sample, () => Speech.Say(p.Jp), out var sampleSec));
            sp.Children.Add(WaveRow("Ты", mine, () => _ = Speech.PlayWavAsync(rec.Wav), out var mineSec).M(0, 8, 0, 0));
            mineSec.Text = $"{rec.Seconds:0.0} с";
            _ = LoadSampleWave(p.Jp, sample, sampleSec);
            sp = sp0;
        }

        var acts = U.Row(8,
            U.Btn("Ещё раз", ToggleRecord, "", ""),
            U.Btn("Медленный образец", () => Speech.Say(p.Jp, true), "", ""),
            U.Btn(_i + 1 < _l.Items.Length ? "Следующая" : "Завершить", () => Go(1), r.Ok ? "Primary" : "", ""));
        acts.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(acts);

        var card = U.Card(sp);
        _result.Content = card;
        Anim.FadeUp(card, 0, 10, 300);
        if (r.Score >= 90) Results.Confetti(_wrap);
        RenderDots();
    }

    public static Control WaveRow(string label, WaveView wave, Action play, out TextBlock sec)
    {
        var g = U.Cols("70", "auto", "*", "56");
        g.Children.Add(U.T(label, "Muted", 12.5).CenterV());
        var pb = new Button { Classes = { "IconButton" }, Content = "", Width = 30, Height = 30, FontSize = 12}.Tip("Воспроизвести");
        pb.Click += (_, _) => play();
        g.Children.Add(pb.Col(1));
        g.Children.Add(wave.M(8, 0, 8, 0).Col(2));
        sec = U.T("", "Muted", 12).CenterV();
        g.Children.Add(sec.Col(3));
        return g;
    }

    public static async Task LoadSampleWave(string jp, WaveView view, TextBlock sec, bool? male = null)
    {
        var path = await Tts.GetAsync(jp, false, male);
        if (path == null) return;
        var (peaks, seconds) = await Task.Run(() => Waveform.PeaksFromFile(path, 90));
        view.SetPeaks(peaks, seconds);
        sec.Text = $"{seconds:0.0} с";
    }

    /// <summary>Посимвольная подсветка: зелёный — совпало, красный — пропущено, жёлтый — лишнее.</summary>
    /// <param name="heardKana">Распознанное, переведённое в кану: сравнение идёт по звучанию, а не по написанию.</param>
    public static Control Diff(string jp, string kanaPlain, string heard, string heardKana = "")
    {
        string h = Speech.Normalize(heard);
        string a = Speech.Normalize(jp), b = Speech.Normalize(Kana.ToHiragana(kanaPlain));
        string hk = heardKana.Length > 0 ? heardKana : Kana.ToHiragana(h);
        bool bySound = Speech.Similarity(h, a) < Speech.Similarity(Reading.Phon(hk), Reading.Phon(b));
        string target = bySound ? b : a;
        string hh = bySound ? hk : h;
        static char F(char c) => c switch { 'は' => 'わ', 'を' => 'お', 'へ' => 'え', 'づ' => 'ず', 'ぢ' => 'じ', _ => c };
        bool Eq(char p, char q) => p == q || F(p) == F(q);
        int n = target.Length, m = hh.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Eq(target[i], hh[j]) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var tb = new TextBlock { FontFamily = U.R<FontFamily>("JpFont"), FontSize = 20, TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection() };
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && Eq(target[x], hh[y])) { tb.Inlines.Add(new Run(target[x].ToString()) { Foreground = U.B("Good") }); x++; y++; }
            else if (y < m && (x >= n || dp[x, y + 1] >= dp[x + 1, y])) { tb.Inlines.Add(new Run(hh[y].ToString()) { Foreground = U.B("Gold"), TextDecorations = TextDecorations.Strikethrough }); y++; }
            else { tb.Inlines.Add(new Run(target[x].ToString()) { Foreground = U.B("Bad"), TextDecorations = TextDecorations.Underline }); x++; }
        }
        var sp = new StackPanel();
        sp.Children.Add(tb);
        if (bySound && h != hk) sp.Children.Add(U.T($"Whisper записал: {heard.Trim()}", "Muted", 12).M(0, 4, 0, 0));
        sp.Children.Add(U.T("зелёный — верно · красный — пропущено · зачёркнуто — лишнее", "Muted", 12).M(0, 4, 0, 0));
        return sp;
    }

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space: ToggleRecord(); return true;
            case Key.L: Speech.Say(P.Jp); return true;
            case Key.S: Speech.Say(P.Jp, true); return true;
            case Key.M: if (_last != null) _ = Speech.PlayWavAsync(_last.Wav); return true;
            case Key.Right: Go(1); return true;
            case Key.Left: Go(-1); return true;
        }
        return false;
    }
}
