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

public sealed class QuizQ
{
    public string Prompt = "";
    public string? Sub;
    public double PromptSize = 92;
    public bool PromptJp = true;
    public string Answer = "";              // для ввода: варианты через '|'
    public string[] Options = Array.Empty<string>();
    public bool OptionsJp;
    public bool Typing;
    public string? Speak;                   // что произнести после ответа
    public bool SpeakOnShow;                // произнести сразу (аудирование)
    public string? Explain;
    public Action<bool>? OnResult;
    public string? Key;                     // для тетради ошибок
}

/// <summary>Квиз: выбор из 4 вариантов или ввод ответа. Комбо, повтор ошибок, итоговый экран.</summary>
public sealed class QuizSession : Grid, IPage, IKeyTarget
{
    public string Title { get; }
    readonly List<QuizQ> _all;
    readonly Action<int, int>? _finished;
    List<QuizQ> _queue = new();
    readonly HashSet<QuizQ> _retried = new();
    int _idx, _correct, _answered, _combo, _bestCombo, _xp;
    bool _locked;
    QuizQ Q => _queue[_idx];

    readonly ProgressBar _bar = new() { Height = 10 };
    readonly TextBlock _count = new() { FontSize = 13, Foreground = U.B("Muted"), VerticalAlignment = VerticalAlignment.Center };
    readonly Border _comboPill;
    readonly TextBlock _comboText = new() { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x00)) };
    readonly Border _promptCard;
    readonly TextBlock _prompt = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _sub = new() { FontSize = 15, Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    readonly UniformGrid _options = new() { Columns = 2, MaxWidth = 760 };
    readonly TextBox _input = new() { MaxWidth = 520, TextAlignment = TextAlignment.Center };
    readonly Grid _typePanel = new() { MaxWidth = 520 };
    readonly Border _feedback;
    readonly TextBlock _fbIcon = new() { FontFamily = U.R<FontFamily>("Icons"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _fbTitle = new() { FontSize = 16, FontWeight = FontWeights.Bold };
    readonly TextBlock _fbText = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = U.B("Text") };
    readonly Button _next;
    readonly Grid _main = new();
    readonly DispatcherTimer _auto;

    public QuizSession(string title, IEnumerable<QuizQ> questions, Action<int, int>? finished = null)
    {
        Title = title;
        _all = questions.ToList();
        _finished = finished;
        _auto = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _auto.Tick += (_, _) => { _auto.Stop(); Next(); };

        Margin = new Thickness(40, 8, 40, 28);
        _main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });
        Children.Add(_main);

        // Верх: прогресс + комбо
        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _bar.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(_bar);
        _count.Margin = new Thickness(16, 0, 0, 0);
        SetColumn(_count, 1); top.Children.Add(_count);
        _comboPill = new Border { Classes = { "Pill" }, Background = U.B("GoldGradient"), Margin = new Thickness(12, 0, 0, 0), Child = _comboText, Opacity = 0 };
        SetColumn(_comboPill, 2); top.Children.Add(_comboPill);
        _main.Children.Add(top);

        // Карточка вопроса
        var speak = U.SpeakBtn(() => Q.Speak ?? Q.Prompt);
        speak.HorizontalAlignment = HorizontalAlignment.Right; speak.VerticalAlignment = VerticalAlignment.Top;
        var promptGrid = new Grid();
        promptGrid.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _prompt, _sub } });
        promptGrid.Children.Add(speak);
        _promptCard = new Border { Classes = { "Card" }, Child = promptGrid, MaxWidth = 760, MinHeight = 240, Padding = new Thickness(28), Margin = new Thickness(0, 8, 0, 20) };
        SetRow(_promptCard, 1); _main.Children.Add(_promptCard);

        // Ответы
        SetRow(_options, 2); _main.Children.Add(_options);
        var check = U.Btn("Проверить", CheckTyped, "Primary");
        check.HorizontalAlignment = HorizontalAlignment.Right; check.Margin = new Thickness(0, 6, 6, 6);
        _typePanel.Children.Add(_input);
        _typePanel.Children.Add(check);
        _input.Padding = new Thickness(16, 12, 140, 12);
        SetRow(_typePanel, 2); _main.Children.Add(_typePanel);

        // Обратная связь
        _next = U.Btn("Далее", Next, "Primary", "");
        _next.FlowDirection = FlowDirection.LeftToRight;
        var fbGrid = new Grid();
        fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fbGrid.ColumnDefinitions.Add(new ColumnDefinition());
        fbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fbGrid.Children.Add(_fbIcon);
        var fbStack = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _fbTitle, _fbText } };
        SetColumn(fbStack, 1); fbGrid.Children.Add(fbStack);
        SetColumn(_next, 2); _next.VerticalAlignment = VerticalAlignment.Center; fbGrid.Children.Add(_next);
        _feedback = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 12, 14, 12), Child = fbGrid, MaxWidth = 760, Margin = new Thickness(0, 16, 0, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0, BorderThickness = new Thickness(1) };
        SetRow(_feedback, 3); _main.Children.Add(_feedback);

        Start();
    }

    void Start()
    {
        Children.Clear(); Children.Add(_main);
        _queue = _all.Shuffled();
        _retried.Clear();
        _idx = _correct = _answered = _combo = _bestCombo = _xp = 0;
        Render();
    }

    void Render()
    {
        _locked = false;
        _auto.Stop();
        var q = Q;
        _bar.Maximum = _queue.Count; _bar.Value = _idx;
        _count.Text = $"{_idx + 1} / {_queue.Count}";

        _prompt.Text = q.Prompt;
        _prompt.FontSize = q.PromptSize;
        _prompt.FontFamily = U.R<FontFamily>(q.PromptJp ? "JpFont" : "UiFont");
        _prompt.FontWeight = q.PromptJp ? FontWeights.Normal : FontWeights.SemiBold;
        _sub.Text = q.Sub ?? "";
        _sub.IsVisible = string.IsNullOrEmpty(q.Sub) ? false : true;
        Anim.FadeUp(_promptCard, 0, 10, 300);
        Anim.Pop(_prompt, 0.9);
        if (q.SpeakOnShow && q.Speak != null) Speech.Say(q.Speak);

        Anim.Opacity(_feedback, _feedback.Opacity, 0, 120);
        _feedback.IsHitTestVisible = false;

        if (q.Typing)
        {
            _options.IsVisible = false;
            _typePanel.IsVisible = true;
            _input.Text = ""; _input.IsEnabled = true;
            Dispatcher.UIThread.Post(() => { _input.Focus(); _input.Focus(); }, DispatcherPriority.Input);
        }
        else
        {
            _typePanel.IsVisible = false;
            _options.IsVisible = true;
            _options.Children.Clear();
            int n = 1;
            foreach (var opt in q.Options)
            {
                var b = OptionButton(n++, opt, q.OptionsJp);
                _options.Children.Add(b);
            }
            Anim.Stagger(_options, 35, 60);
            Focus();
        }
    }

    Button OptionButton(int n, string text, bool jp)
    {
        var g = new Grid();
        g.Children.Add(new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = U.B("Surface2"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 0, 0),
            Child = new TextBlock { Text = n.ToString(), FontSize = 12, FontFamily = U.R<FontFamily>("UiFont"), Foreground = U.B("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        g.Children.Add(new TextBlock
        {
            Text = text, FontSize = jp ? 26 : 18, FontFamily = U.R<FontFamily>(jp ? "JpFont" : "UiFont"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(48, 8, 48, 8),
        });
        var b = new Button { Classes = { "Option" }, Content = g, Margin = new Thickness(6), Tag = text };
        b.Click += (_, _) => Choose(b);
        return b;
    }

    void Choose(Button b)
    {
        if (_locked) return;
        bool ok = (string)b.Tag == Q.Answer;
        foreach (Button o in _options.Children)
        {
            if ((string)o.Tag == Q.Answer) { o.Background = U.B("GoodSoft"); o.BorderBrush = U.B("Good"); }
            else if (o == b) { o.Background = U.B("BadSoft"); o.BorderBrush = U.B("Bad"); }
            else o.Opacity = 0.45;
        }
        if (ok) Anim.Pop(b, 0.92); else Anim.Shake(b);
        Answered(ok);
    }

    void CheckTyped()
    {
        if (_locked) return;
        var text = _input.Text.Trim().ToLowerInvariant().Replace(" ", "");
        if (text.Length == 0) return;
        var alts = Q.Answer.Split('|').Select(a => a.Trim().ToLowerInvariant().Replace(" ", ""));
        bool ok = alts.Contains(text);
        _input.IsEnabled = false;
        if (!ok) Anim.Shake(_typePanel); else Anim.Pop(_typePanel, 0.95);
        Answered(ok);
    }

    void Answered(bool ok)
    {
        _locked = true;
        _answered++;
        var q = Q;
        q.OnResult?.Invoke(ok);
        if (ok) { Mistakes.Correct(q.Key); Sfx.Ok(); } else { Mistakes.Add(q.Key); Sfx.Bad(); }
        if (ok)
        {
            _correct++; _combo++; _bestCombo = Math.Max(_bestCombo, _combo);
            int gain = 2 + Math.Min(_combo / 3, 3);
            _xp += gain;
        }
        else
        {
            _combo = 0;
            if (_retried.Add(q)) _queue.Add(q); // ошибку повторим в конце
        }
        _comboText.Text = $"連 ×{_combo}";
        Anim.Opacity(_comboPill, _comboPill.Opacity, _combo >= 3 ? 1 : 0, 200);
        if (_combo >= 3) Anim.Pop(_comboPill, 0.7);

        if (ProgressStore.P.AutoSpeak && q.Speak != null) Speech.Say(q.Speak);

        var first = q.Answer.Split('|')[0];
        _feedback.Background = U.B(ok ? "GoodSoft" : "BadSoft");
        _feedback.BorderBrush = U.B(ok ? "Good" : "Bad");
        _fbIcon.Text = ok ? "" : "";
        _fbIcon.Foreground = U.B(ok ? "Good" : "Bad");
        _fbTitle.Text = ok ? Praise() : $"Правильно: {first}";
        _fbTitle.Foreground = U.B(ok ? "Good" : "Bad");
        _fbText.Text = q.Explain ?? (ok ? "" : "Этот вопрос вернётся в конце.");
        _fbText.IsVisible = string.IsNullOrEmpty(_fbText.Text) ? false : true;
        _feedback.IsHitTestVisible = true;
        Anim.FadeUp(_feedback, 0, 8, 220);

        _bar.Value = _idx + 1;
        if (ok && q.Explain == null) _auto.Start();
    }

    static readonly string[] Praises = { "Верно!", "Отлично!", "Так держать!", "正解！", "Супер!", "いいね！" };
    string Praise() => _combo >= 5 ? $"Комбо ×{_combo}! すごい！" : Praises[U.Rng.Next(Praises.Length)];

    void Next()
    {
        if (!_locked) return;
        _auto.Stop();
        if (_idx + 1 >= _queue.Count) { Finish(); return; }
        _idx++;
        Render();
    }

    void Finish()
    {
        _locked = true;
        int firstTry = _all.Count;
        int pct = _answered == 0 ? 0 : (int)Math.Round(100.0 * _correct / _answered);
        ProgressStore.AddXp(_xp);
        Sfx.Done();
        _finished?.Invoke(_correct, _answered);
        Children.Clear();
        Children.Add(Results.Build(pct, _xp, new[]
        {
            ("Вопросов", firstTry.ToString()),
            ("Верных ответов", $"{_correct} / {_answered}"),
            ("Лучшее комбо", $"×{_bestCombo}"),
        }, Start, Shell.Back, this));
    }

    public bool HandleKey(Key key)
    {
        if (Children.Count == 0 || Children[0] != _main) return false;
        if (key is Key.Enter or Key.Space)
        {
            if (_locked) { Next(); return true; }
            if (Q.Typing && key == Key.Enter) { CheckTyped(); return true; }
            return false;
        }
        if (!_locked && !Q.Typing)
        {
            int n = key switch { Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2, Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4, _ => 0 };
            if (n > 0 && n <= _options.Children.Count) { Choose((Button)_options.Children[n - 1]); return true; }
        }
        return false;
    }

    /// <summary>Собирает вопрос с 4 вариантами: правильный + 3 случайных отвлекающих.</summary>
    public static string[] MakeOptions(string answer, IEnumerable<string> pool, int count = 4)
    {
        var wrong = pool.Where(p => p != answer).Distinct().Shuffled().Take(count - 1);
        return wrong.Append(answer).Shuffled().ToArray();
    }
}
