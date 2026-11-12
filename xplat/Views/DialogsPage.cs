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

public sealed class DialogsPage : UserControl, IPage
{
    public string Title => "Диалоги";

    public void OnShown()
    {
        var root = new StackPanel();
        root.Children.Add(U.Header("対", "Диалоги", "Живые сценки: собеседника озвучивает нейро-голос, свою роль ты говоришь вслух."));
        if (!Asr.Ready) root.Children.Add(SpeechUi.ModelCard(OnShown).M(0, 0, 0, 20));
        foreach (var level in new[] { "N5", "N4" })
        {
            root.Children.Add(U.T(level == "N5" ? "Начальный уровень · N5" : "Продолжающий уровень · N4", "H2").M(0, 6, 0, 12));
            var grid = new UniformGrid { Columns = U.Columns(2), Margin = new Thickness(-6, 0, -6, 14) };
            foreach (var d in Db.Dialogs.Where(x => x.Level == level)) grid.Children.Add(Card(d));
            root.Children.Add(grid);
            Anim.Stagger(grid, 35, 60);
        }
        Content = U.Page(root);
    }

    static Border Card(Dialog d)
    {
        bool done = ProgressStore.P.Flags.Contains("dialog:" + d.Id);
        var g = U.Cols("auto", "*", "auto");
        g.Children.Add(Emoji.Img(Emoji.For(d.Title, "speech_balloon"), 48));
        g.Children.Add(U.Stack(U.T(d.Title, "H3", 15), U.T($"{d.Desc} · {d.Lines.Length} реплик", "Muted")).CenterV().M(14, 0, 8, 0).Col(1));
        g.Children.Add(new TextBlock { Text = done ? "" : "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B(done ? "Good" : "Faint"), VerticalAlignment = VerticalAlignment.Center }.Col(2));
        var card = new Border { Classes = { "Card" }, Child = g, Margin = new Thickness(6), Padding = new Thickness(16), Cursor = Cursors.Hand };
        HomePage.Hover(card);
        card.PointerReleased += (_, _) => Shell.OpenFrom(card, new DialogPage(d));
        return card;
    }
}

/// <summary>Сценка по ролям: чат-пузыри, озвучка собеседника, запись и оценка своих реплик.</summary>
public sealed class DialogPage : UserControl, IPage, IKeyTarget, IResumable
{
    readonly Dialog _d;
    public string ResumeKey => $"dialog:{_d.Id}";
    public string ResumeTitle => $"Диалог · {_d.Title}";
    int _role = 1;
    public string Title => _d.Title;
    readonly StackPanel _chat = new() { Margin = new Thickness(0, 0, 14, 0) }; // место под полосу прокрутки — реплики справа не обрезаются
    readonly ScrollViewer _scroll;
    readonly List<Border> _bubbles = new();
    readonly List<Control> _full = new();   // полное содержимое реплики (для режима «по памяти»)
    bool _recall;
    internal bool RecallPreview { set { _recall = value; } }
    readonly TextBlock _status = U.T("", "Muted", 13);
    readonly SpeechUi.LevelMeter _meter = new();
    readonly Panel _controls = Shell.Compact ? new WrapPanel() : new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    CancellationTokenSource? _cts;
    TaskCompletionSource<bool>? _wait;   // ожидание «Дальше / Повторить»
    readonly List<int> _scores = new();

    public DialogPage(Dialog d)
    {
        _d = d;
        var root = new Grid { Margin = Shell.Compact ? new Thickness(16, 8, 16, 12) : new Thickness(40, 8, 40, 16), MaxWidth = 900 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = U.Cols("*", "auto");
        head.Children.Add(U.Stack(U.T(d.Title, "H1", 24), U.T(d.Desc, "Muted")));
        var roles = U.Row(0); roles.VerticalAlignment = VerticalAlignment.Center;
        roles.Children.Add(U.T("Моя роль:", "Muted").CenterV().M(0, 0, 10, 0));
        for (int r = 0; r < d.Roles.Length; r++)
        {
            int rr = r;
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = d.Roles[r], GroupName = "role", IsChecked = r == _role };
            rb.Checked += (_, _) => { _role = rr; Stop(); BuildChat(); };
            roles.Children.Add(rb);
        }
        head.Children.Add(roles.Col(1));
        root.Children.Add(U.Stackify(head));

        _scroll = new ScrollViewer { Content = _chat, Margin = new Thickness(0, 16, 0, 12) };
        root.Children.Add(_scroll.RowAt(1));

        var bar = new Border { Classes = { "Card" }, Padding = new Thickness(16, 10, 10, 10) };
        var bg = U.Cols("auto", "*", "auto");
        bg.Children.Add(_meter.CenterV().M(0, 0, 14, 0));
        bg.Children.Add(_status.CenterV().Col(1));
        bg.Children.Add(_controls.Col(2));
        bar.Child = U.Stackify(bg);
        root.Children.Add(bar.RowAt(2));
        Content = root;
        Unloaded += (_, _) => Stop();
        BuildChat();
        Asr.Warmup();
    }

    void BuildChat()
    {
        _chat.Children.Clear();
        _bubbles.Clear();
        _full.Clear();
        foreach (var line in _d.Lines)
        {
            bool mine = line.Speaker == _role;
            var sp = new StackPanel();
            sp.Children.Add(U.T(_d.Roles[line.Speaker], "Overline").M(0, 0, 0, 4));
            var jpRow = U.Cols("*", "auto");
            jpRow.Children.Add(U.Jp(line.Jp, 18));
            var l = line;
            var play = new Button { Classes = { "IconButton" }, Content = "", Width = 30, Height = 30, FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
            play.Click += (_, _) => _ = Speech.SayAsync(l.Jp, false, _d.Male[l.Speaker]);
            jpRow.Children.Add(play.Col(1));
            sp.Children.Add(jpRow);
            sp.Children.Add(U.T(line.Romaji, "Muted", 12.5).M(0, 2, 0, 0));
            sp.Children.Add(U.T(line.Ru, "Body", 13, U.B("Muted")).M(0, 2, 0, 0));
            _full.Add(sp);
            Control child = sp;
            if (mine && _recall)
                child = U.Stack(U.T(_d.Roles[line.Speaker], "Overline").M(0, 0, 0, 4),
                    U.T("Скажи по-японски:", "Muted", 12),
                    U.T("«" + line.Ru + "»", "H3", 16).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 2, 0, 0));
            var bubble = new Border
            {
                Background = U.B(mine ? "AccentSoft" : "Surface"), BorderBrush = U.B(mine ? "AccentSoft" : "Stroke"), BorderThickness = new Thickness(1),
                CornerRadius = mine ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4),
                Padding = new Thickness(16, 12, 10, 12), MaxWidth = 560, Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, Child = child,
            };
            _bubbles.Add(bubble);
            _chat.Children.Add(bubble);
        }
        Anim.Stagger(_chat, 30, 0, 12);
        SetIdle();
    }

    void SetIdle()
    {
        _controls.Children.Clear();
        _controls.Children.Add(U.Btn("Прослушать всё", () => Run(listenOnly: true), "", "").M(0, 0, 8, 0));
        _controls.Children.Add(U.Btn("По памяти", () => { _recall = true; BuildChat(); Run(listenOnly: false); }, "", "").M(0, 0, 8, 0).Tip("Твои реплики скрыты — вспомни фразу по переводу"));
        _controls.Children.Add(U.Btn("Играть роль", () => { if (_recall) { _recall = false; BuildChat(); } Run(listenOnly: false); }, "Primary", ""));
        _status.Text = Asr.Ready ? $"Ты — «{_d.Roles[_role]}». Нажми «Играть роль»: собеседник говорит, ты отвечаешь вслух."
                                 : "Можно прослушать диалог. Для проверки своих реплик скачай модель Whisper.";
        foreach (var b in _bubbles) b.Opacity = 1;
    }

    void Stop()
    {
        _cts?.Cancel();
        _wait?.TrySetResult(false);
        Speech.Stop();
        _meter.Attach(null);
    }

    async void Run(bool listenOnly)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _scores.Clear();
        _controls.Children.Clear();
        _controls.Children.Add(U.Btn("Стоп", () => { Stop(); SetIdle(); }, "", ""));
        try
        {
            for (int i = 0; i < _d.Lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var line = _d.Lines[i];
                Focus(i);
                bool mine = line.Speaker == _role && !listenOnly;
                if (!mine)
                {
                    _status.Text = $"{_d.Roles[line.Speaker]} говорит…";
                    await Speech.SayAsync(line.Jp, false, _d.Male[line.Speaker]);
                    await Task.Delay(350, ct);
                    continue;
                }
                // Моя реплика
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Asr.Ready)
                    {
                        _status.Text = "Твоя реплика — скажи её вслух, затем нажми «Дальше»";
                        await AskNext(false);
                        break;
                    }
                    _status.Text = "Твоя очередь — включаю микрофон…";
                    using var rec = new Recorder();
                    rec.Started += () => Dispatcher.UIThread.Post(() => _status.Text = "Твоя очередь — говори!");
                    _meter.Attach(rec);
                    var (check, _) = await SpeechUi.RecordAndScore(line.Jp, line.KanaPlain, rec, ct, s => _status.Text = s);
                    _meter.Attach(null);
                    if (check.Error != null) { _status.Text = check.Error; if (await AskNext(true)) break; continue; }
                    if (_recall && _bubbles[i].Child != _full[i]) { _bubbles[i].Child = _full[i]; Anim.Pop(_bubbles[i], 0.95); }
                    MarkScore(i, check.Score);
                    if (check.Heard.Length > 0) ProgressStore.SpeechResult(line.Id, check.Score);
                    _status.Text = $"{check.Score} — {check.Verdict}";
                    if (check.Ok) { _scores.Add(check.Score); await Task.Delay(700, ct); break; }
                    // Слабый результат: даём повторить
                    if (await AskNext(true)) { _scores.Add(check.Score); break; }
                    // false означает «повторить» — Speech образца и снова запись
                    await Speech.SayAsync(line.Jp, true, _d.Male[line.Speaker]);
                }
            }
            Finish(listenOnly);
        }
        catch (OperationCanceledException) { }
        finally { _meter.Attach(null); }
    }

    /// <summary>Ждёт нажатия «Дальше» (true) или «Повторить» (false).</summary>
    Task<bool> AskNext(bool canRetry)
    {
        _wait = new TaskCompletionSource<bool>();
        _controls.Children.Clear();
        if (canRetry) _controls.Children.Add(U.Btn("Повторить", () => _wait?.TrySetResult(false), "", "").M(0, 0, 8, 0));
        _controls.Children.Add(U.Btn("Дальше", () => _wait?.TrySetResult(true), "Primary", "").M(0, 0, 8, 0));
        _controls.Children.Add(U.Btn("Стоп", () => { Stop(); SetIdle(); }, "Ghost", ""));
        var t = _wait.Task;
        t.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _controls.Children.Clear();
                _controls.Children.Add(U.Btn("Стоп", () => { Stop(); SetIdle(); }, "", ""));
            }
        }));
        return t;
    }

    void Focus(int i)
    {
        for (int k = 0; k < _bubbles.Count; k++)
            Anim.Opacity(_bubbles[k], _bubbles[k].Opacity, k == i ? 1 : k < i ? 0.55 : 0.3, 250);
        _bubbles[i].BringIntoView();
        Anim.Pop(_bubbles[i], 0.97);
    }

    void MarkScore(int i, int score)
    {
        if (_bubbles[i].Child is StackPanel sp)
        {
            if (sp.Children[0] is Border old && old.Tag as string == "score") sp.Children.RemoveAt(0);
            var pill = U.Pill($"{score}", U.B(score >= 75 ? "GoodSoft" : score >= 50 ? "AccentSoft" : "BadSoft"), U.B(score >= 75 ? "Good" : score >= 50 ? "Gold" : "Bad"));
            pill.Tag = "score"; pill.HorizontalAlignment = HorizontalAlignment.Right; pill.Margin = new Thickness(0, 0, 0, -18);
            sp.Children.Insert(0, pill);
            Anim.Pop(pill, 0.6);
        }
    }

    void Finish(bool listenOnly)
    {
        if (!listenOnly)
        {
            ProgressStore.P.Flags.Add("dialog:" + _d.Id);
            ProgressStore.AddXp(15);
        }
        int avg = _scores.Count > 0 ? (int)_scores.Average() : 0;
        _status.Text = listenOnly ? "Диалог прослушан. Теперь попробуй сыграть роль!" : _scores.Count > 0 ? $"Диалог пройден! Средняя оценка: {avg}. +15 XP" : "Диалог пройден! +15 XP";
        if (!listenOnly && avg >= 80 && Content is Panel host) Results.Confetti(host);
        _controls.Children.Clear();
        _controls.Children.Add(U.Btn("Ещё раз", () => Run(listenOnly), "", "").M(0, 0, 8, 0));
        _controls.Children.Add(U.Btn("К диалогам", Shell.Back, "Primary", ""));
        foreach (var b in _bubbles) Anim.Opacity(b, b.Opacity, 1, 300);
    }

    public bool HandleKey(Key key)
    {
        if (_wait is { Task.IsCompleted: false })
        {
            if (key is Key.Enter or Key.Space) { _wait.TrySetResult(true); return true; }
            if (key == Key.R) { _wait.TrySetResult(false); return true; }
        }
        return false;
    }
}
