using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Matsubakira.Services;
using RadioButton = Matsubakira.Compat.RadioButton;

namespace Matsubakira.Views;

/// <summary>
/// Свободный разговор на японском с локальной языковой моделью: собеседник говорит коротко и просто,
/// ты отвечаешь голосом (Whisper) или текстом, приложение подсказывает, как сказать естественнее.
/// </summary>
public sealed class ChatPage : UserControl, IPage, IKeyTarget
{
    public string Title => "Разговор с ИИ";

    static ChatPrompts.Scene[] Scenes => ChatPrompts.Scenes;
    ChatPrompts.Scene Scene => Scenes.First(x => x.Key == _scene);

    string _scene = "free";
    string _level = ProgressStore.P.Level is "N4" or "N3" ? ProgressStore.P.Level : "N5";
    readonly List<(string role, string text)> _history = new();
    readonly StackPanel _messages = new() { Spacing = 10 };
    readonly ScrollViewer _scroll;
    readonly TextBox _input = new() { FontSize = 16, Padding = new Thickness(14, 10), Watermark = "Ответь по-японски (можно латиницей: konnichiwa) или нажми на микрофон" };
    readonly Button _send, _mic, _hint;
    readonly TextBlock _status = U.T("", "Muted", 12.5);
    readonly SpeechUi.LevelMeter _meter = new();
    readonly ContentControl _top = new();
    CancellationTokenSource? _cts;
    Recorder? _rec;
    bool _busy, _recording;

    public ChatPage()
    {
        _send = U.Btn("", () => _ = SendAsync(), "Primary", "").Tip("Отправить (Enter)");
        _mic = U.Btn("", ToggleMic, "", "").Tip("Сказать голосом (пробел, когда поле ввода пустое)");
        _hint = U.Btn("Подсказка", () => _ = HintAsync(), "Ghost", "").Tip("Пример ответа, если не знаешь, что сказать");
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = SendAsync(); } };
        _scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(0, 4, 8, 4), Child = _messages }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Unloaded += (_, _) => { _cts?.Cancel(); _rec?.Stop(); };
        Render();
    }

    public void OnShown() { _ = JDict.LoadAsync(); if (Llm.Ready) Llm.Warmup(); Asr.Warmup(); }

    void Render()
    {
        // Повторная отрисовка (после скачивания модели, смены сцены) переиспользует эти элементы —
        // сначала отцепляем их от старой разметки, иначе Avalonia бросает «already has a visual parent»
        foreach (var c in new Control[] { _top, _scroll, _mic, _input, _send, _hint, _status, _meter })
            switch (c.Parent)
            {
                case Panel p: p.Children.Remove(c); break;
                case Decorator d: d.Child = null; break;
                case ContentControl cc: cc.Content = null; break;
            }
        var root = new Grid { RowDefinitions = new RowDefinitions("auto,auto,*,auto"), Margin = new Thickness(40, 12, 40, 18), MaxWidth = 980 };
        root.Children.Add(U.Header("話", "Разговор с ИИ", "Свободный диалог на японском с нейросетью, которая работает прямо на этом компьютере — без интернета."));
        root.Children.Add(_top.RowAt(1));
        if (!Llm.Ready)
        {
            _top.Content = ModelCard();
            Content = root;
            return;
        }
        // сцена и уровень
        var scenes = new WrapPanel();
        foreach (var sc in Scenes)
        {
            var (key, title, emoji) = (sc.Key, sc.Title, sc.Emoji);
            var rb = new RadioButton { Classes = { "ChipRadio" }, GroupName = "scene", IsChecked = _scene == key, Margin = new Thickness(0, 0, 8, 8),
                Content = U.Row(6, Emoji.Img(emoji, 18), new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }) };
            var k = key;
            rb.Checked += (_, _) => { if (_scene != k) { _scene = k; Restart(); } };
            scenes.Children.Add(rb);
        }
        var levels = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var l in new[] { "N5", "N4", "N3" })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = l, GroupName = "clevel", IsChecked = _level == l };
            var ll = l;
            rb.Checked += (_, _) => { if (_level != ll) { _level = ll; Restart(); } };
            levels.Children.Add(rb);
        }
        var bar = U.Cols("*", "auto");
        bar.Children.Add(scenes);
        bar.Children.Add(U.Row(10, levels, U.Btn("Заново", Restart, "Ghost", "")).Col(1).Also(r => r.VerticalAlignment = VerticalAlignment.Top));
        _top.Content = U.Stack(bar, U.T($"Модель: {Llm.Info.Title} ({Llm.Info.Desc.Split(" · ")[0]})", "Muted", 11.5).M(0, 2, 0, 10));

        root.Children.Add(new Border { Classes = { "Card" }, Padding = new Thickness(18, 14), Child = _scroll }.RowAt(2));

        var input = U.Cols("auto", "*", "auto", "auto");
        input.Margin = new Thickness(0, 12, 0, 0);
        input.Children.Add(_mic.Also(b => { b.Width = 48; b.Height = 48; b.Padding = new Thickness(0); }));
        input.Children.Add(_input.Col(1).M(10, 0, 10, 0));
        input.Children.Add(_send.Col(2).Also(b => { b.Width = 48; b.Height = 48; b.Padding = new Thickness(0); }));
        input.Children.Add(_hint.Col(3).M(8, 0, 0, 0));
        var bottom = U.Stack(input, U.Cols("*", "auto").Also(g => { g.Children.Add(_status.M(2, 6, 0, 0)); g.Children.Add(_meter.Col(1)); }));
        root.Children.Add(bottom.RowAt(3));
        Content = root;
        if (_history.Count == 0) _ = StartAsync();
    }

    // ─────────── Модель ───────────
    Control ModelCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T("Нужна языковая модель", "H3"));
        sp.Children.Add(U.T("Скачивается один раз (1–2 ГБ) и работает офлайн на процессоре. Ответ появляется за несколько секунд.", "Muted").M(0, 4, 0, 12));
        var choose = new StackPanel();
        foreach (var m in Llm.Models)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, GroupName = "llm", IsChecked = Llm.ModelId == m.Id, Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = U.Stack(new TextBlock { Text = $"{m.Title} · {m.Size / 1_000_000_000.0:0.0} ГБ", FontWeight = FontWeight.SemiBold }, U.T($"{m.Desc} · лицензия {m.License}", "Muted", 12)) };
            var id = m.Id;
            rb.Checked += (_, _) => { ProgressStore.P.LlmModel = id; ProgressStore.Notify(); };
            choose.Children.Add(rb);
        }
        sp.Children.Add(choose);
        var bar = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
        var status = U.T("", "Muted").M(0, 6, 0, 0);
        var btn = U.Btn("Скачать модель", () => { }, "Primary", "");
        btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 6, 0, 0);
        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false; choose.IsEnabled = false; bar.IsVisible = true;
            var id = string.IsNullOrEmpty(ProgressStore.P.LlmModel) ? Llm.Models[0].Id : ProgressStore.P.LlmModel;
            var size = Llm.Models.First(x => x.Id == id).Size;
            var prog = new Progress<double>(v => { bar.Value = v; status.Text = $"Скачано {v * size / 1e9:0.00} из {size / 1e9:0.00} ГБ"; });
            try
            {
                await Llm.DownloadAsync(id, prog, CancellationToken.None);
                ProgressStore.P.LlmModel = id; ProgressStore.Notify();
                Shell.Toast("Языковая модель установлена — можно разговаривать", "話");
                Llm.Warmup();
                Render();
            }
            catch (Exception ex) { status.Text = "Ошибка загрузки: " + ex.Message + " (нажми ещё раз — загрузка продолжится)"; btn.IsEnabled = true; choose.IsEnabled = true; }
        };
        sp.Children.Add(btn); sp.Children.Add(bar); sp.Children.Add(status);
        return U.Card(sp);
    }

    // ─────────── Разговор ───────────
    string SystemPrompt() => ChatPrompts.System(Scene, _level);

    void Restart()
    {
        _cts?.Cancel();
        _history.Clear();
        _messages.Children.Clear();
        Render();
    }

    async Task StartAsync()
    {
        _history.Add(("system", SystemPrompt()));
        await ReplyAsync(("user", ChatPrompts.Opener(Scene)), hiddenUser: true);
    }

    async Task SendAsync()
    {
        if (_busy || !Llm.Ready) return;
        var text = (_input.Text ?? "").Trim();
        if (text.Length == 0) return;
        // латиницу переводим в кану: «watashi wa» → «わたしは»
        if (text.All(c => c < 128)) text = Kana.FromRomaji(text.ToLowerInvariant()).Replace(" ", "");
        _input.Text = "";
        var (bubble, extra) = UserBubble(text);
        _messages.Children.Add(bubble);
        ScrollEnd();
        await ReplyAsync(("user", text));
        _ = CorrectAsync(text, extra);
    }

    async Task ReplyAsync((string role, string text) msg, bool hiddenUser = false)
    {
        _busy = true; SetBusy(true);
        _history.Add(msg);
        var (bubble, body, tools) = AiBubble();
        _messages.Children.Add(bubble);
        ScrollEnd();
        _status.Text = "Собеседник думает…";
        var sb = new StringBuilder();
        _cts = new CancellationTokenSource();
        try
        {
            // последние сообщения: система + до 10 реплик — модель маленькая, длинный контекст ей вредит
            var ctx = _history.Take(1).Concat(_history.Skip(1).TakeLast(10)).ToList();
            await foreach (var piece in Llm.ChatAsync(ctx, 140, 0.7f, _cts.Token))
            {
                sb.Append(piece);
                body.Text = OnlyJapanese(Llm.Clean(sb.ToString()));
                ScrollEnd();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { body.Text = "（ошибка модели）"; _status.Text = "Ошибка: " + ex.Message; }
        var reply = OnlyJapanese(Llm.Clean(sb.ToString()));
        if (reply.Length == 0) reply = "すみません、もう一度お願いします。";
        body.Text = reply;
        _history.Add(("assistant", reply));
        FillAiTools(tools, reply);
        if (hiddenUser) _history.RemoveAt(_history.Count - 2); // служебную просьбу «начни разговор» из истории убираем
        _status.Text = "Твоя очередь: ответь голосом или текстом";
        _busy = false; SetBusy(false);
        if (ProgressStore.P.ChatAutoSpeak) Speech.Say(reply);
        ProgressStore.AddXp(2);
    }

    /// <summary>Оставляет только японский текст: маленькие модели иногда добавляют перевод или ромадзи.</summary>
    static string OnlyJapanese(string s)
    {
        var lines = s.Split('\n').Select(l => l.Trim()).Where(l => l.Any(c => Kana.IsKana(c) || Reading.IsKanji(c))).ToList();
        var t = string.Join(" ", lines);
        // «(Перевод: …)» и латиница в скобках
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[（(][^）)]*[A-Za-zА-Яа-яЁё][^）)]*[）)]", "");
        return t.Trim();
    }

    (Control bubble, StackPanel extra) UserBubble(string text)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = text, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 18, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(U.T(Reading.RomajiWords(text), "Muted", 12));
        var extra = new StackPanel();
        sp.Children.Add(extra);
        var b = new Border { Child = sp, Background = U.B("AccentSoft"), CornerRadius = new CornerRadius(16, 16, 4, 16), Padding = new Thickness(14, 10), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 620 };
        Anim.FadeUp(b, 0, 8, 240);
        return (b, extra);
    }

    (Control bubble, TextBlock body, StackPanel tools) AiBubble()
    {
        var body = new TextBlock { Text = "…", FontFamily = U.R<FontFamily>("JpFont"), FontSize = 19, TextWrapping = TextWrapping.Wrap };
        var tools = new StackPanel();
        var sp = U.Stack(body, tools);
        var avatar = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = U.B("Surface3"), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "先", FontFamily = U.R<FontFamily>("JpSerif"), Foreground = U.B("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var b = new Border { Child = sp, Background = U.B("Surface2"), CornerRadius = new CornerRadius(16, 16, 16, 4), Padding = new Thickness(14, 10), MaxWidth = 640 };
        var row = U.Row(10, avatar, b);
        Anim.FadeUp(row, 0, 8, 240);
        return (row, body, tools);
    }

    void FillAiTools(StackPanel tools, string reply)
    {
        tools.Children.Clear();
        tools.Children.Add(U.T(Reading.RomajiWords(reply), "Muted", 12).M(0, 2, 0, 6));
        var tr = U.T("", "Body", 13, U.B("Muted"));
        tr.IsVisible = false;
        var trBtn = U.Btn("Перевод", () => { }, "Ghost", "");
        trBtn.Click += async (_, _) =>
        {
            if (tr.IsVisible) { tr.IsVisible = false; return; }
            tr.IsVisible = true;
            if (tr.Text is { Length: > 0 }) return;
            tr.Text = "перевожу…";
            try { tr.Text = await Llm.AskAsync(ChatPrompts.Translate(reply), 120, 0.2f); }
            catch (Exception ex) { tr.Text = "Не удалось перевести: " + ex.Message; }
        };
        tools.Children.Add(U.Row(2, U.SpeakBtn(() => reply), U.SpeakBtn(() => reply, true), trBtn));
        tools.Children.Add(tr);
    }

    /// <summary>Проверка фразы ученика: если есть ошибка — естественный вариант с озвучкой.</summary>
    async Task CorrectAsync(string text, StackPanel extra)
    {
        if (text.Length < 2) return;
        try
        {
            // сначала надёжные правила (мгновенно), затем — языковая модель
            string? why = null, ans;
            if (GrammarRules.Check(text) is { } rule) (ans, why) = rule;
            else
            {
                if (!Llm.Ready) return;
                ans = await Llm.AskAsync(ChatPrompts.Correct(text), 80, 0.1f);
                ans = OnlyJapanese(ans.Replace("「", "").Replace("」", "")).Trim();
                if (ans.Length == 0 || ans.Contains("OK") || Reading.Phon(Reading.Of(ans)) == Reading.Phon(Reading.Of(text))) return;
            }
            var row = U.Row(6, new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Center },
                U.T("Естественнее: ", "Muted", 12.5).CenterV(),
                new TextBlock { Text = ans, FontFamily = U.R<FontFamily>("JpFont"), FontSize = 15, Foreground = U.B("Gold"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center },
                U.SpeakBtnSmall(ans));
            extra.Children.Add(row.M(0, 6, 0, 0));
            if (why != null) extra.Children.Add(U.T(why, "Muted", 12).M(22, 0, 0, 0));
            Anim.FadeUp(row, 0, 6, 220);
        }
        catch { }
    }

    /// <summary>Подсказка: короткий пример ответа на последнюю реплику собеседника.</summary>
    async Task HintAsync()
    {
        if (_busy || !Llm.Ready) return;
        var last = _history.LastOrDefault(m => m.role == "assistant").text;
        if (string.IsNullOrEmpty(last)) return;
        _hint.IsEnabled = false; _status.Text = "Придумываю пример ответа…";
        try
        {
            var ans = OnlyJapanese(await Llm.AskAsync(ChatPrompts.Hint(Scene, _level, last), 60, 0.5f));
            if (ans.Length == 0) return;
            var chip = U.Btn(ans, () => { _input.Text = ans; _input.Focus(); }, "", null).Tip("Нажми, чтобы вставить в поле ответа, или повтори вслух");
            chip.FontFamily = U.R<FontFamily>("JpFont"); chip.FontWeight = FontWeight.Normal;
            var row = U.Row(8, U.T("Можно ответить:", "Muted", 12.5).CenterV(), chip, U.SpeakBtnSmall(ans));
            row.HorizontalAlignment = HorizontalAlignment.Right;
            _messages.Children.Add(row);
            ScrollEnd();
            Speech.Say(ans);
            _status.Text = "Повтори вслух или нажми на подсказку";
        }
        catch (Exception ex) { _status.Text = "Ошибка: " + ex.Message; }
        finally { _hint.IsEnabled = true; }
    }

    // ─────────── Голос ───────────
    async void ToggleMic()
    {
        if (_recording) { _rec?.Stop(); return; }
        if (_busy) return;
        if (!Asr.Ready) { Shell.Toast("Для голоса нужна модель распознавания (раздел «Говорение»)", "!"); return; }
        if (!Recorder.HasMicrophone) { _status.Text = "Микрофон не найден."; return; }
        Speech.Stop();
        _recording = true;
        _rec = new Recorder(manual: false, maxSeconds: 15);
        _meter.Attach(_rec);
        _mic.Classes.Add("Primary");
        _status.Text = "Включаю микрофон…";
        _rec.Started += () => Dispatcher.UIThread.Post(() => { if (_recording) _status.Text = "Говори! Запись остановится после паузы"; });
        try
        {
            var r = await _rec.RecordAsync(CancellationToken.None);
            _meter.Attach(null); _mic.Classes.Remove("Primary"); _recording = false;
            if (r.Seconds < 0.2 || (!r.Heard && r.PeakLevel < 0.02f)) { _status.Text = "Голос не услышан — говори громче или проверь микрофон."; return; }
            _status.Text = "Распознаю…";
            await JDict.LoadAsync();
            // Реплика собеседника — подсказка контекста: ответ на «何が好きですか» распознаётся точнее
            var last = _history.LastOrDefault(m => m.role == "assistant").text;
            var a = await Asr.TranscribeDetailedAsync(r.Wav, CancellationToken.None, last);
            var text = Speech.DropHallucination(a.Text, last ?? "").Trim();
            if (Speech.Normalize(text).Length == 0) { _status.Text = "Не удалось распознать — попробуй ещё раз, чуть громче."; return; }
            _input.Text = text;
            await SendAsync();
        }
        catch (Exception ex) { _status.Text = "Микрофон: " + ex.Message; }
        finally { _recording = false; _meter.Attach(null); _mic.Classes.Remove("Primary"); _rec?.Dispose(); _rec = null; }
    }

    void SetBusy(bool on) { _send.IsEnabled = !on; _hint.IsEnabled = !on; _mic.IsEnabled = !on; }

    void ScrollEnd() => Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    public bool HandleKey(Key key)
    {
        if (key == Key.Space && string.IsNullOrEmpty(_input.Text) && FocusManagerFocusedIsNotInput()) { ToggleMic(); return true; }
        return false;
    }

    bool FocusManagerFocusedIsNotInput() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() != _input;
}
