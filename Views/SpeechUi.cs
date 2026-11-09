using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Общие элементы для практики произношения: запись → Whisper → оценка; карточка модели; индикатор громкости.</summary>
public static class SpeechUi
{
    public static async Task<(SpeechCheck check, Recording? rec)> RecordAndScore(string jp, string kana, Recorder recorder, CancellationToken ct)
    {
        if (!Recorder.HasMicrophone) return (new SpeechCheck(false, 0, "", "", "Микрофон не найден. Подключи микрофон и попробуй снова."), null);
        if (!Asr.Ready) return (new SpeechCheck(false, 0, "", "", "Модель распознавания ещё не скачана."), null);
        Speech.Stop();
        Recording r;
        try { r = await recorder.RecordAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (new SpeechCheck(false, 0, "", "", "Не удалось включить микрофон: " + ex.Message), null); }
        return (await ScoreRecording(jp, kana, r, ct), r);
    }

    public static async Task<SpeechCheck> ScoreRecording(string jp, string kana, Recording r, CancellationToken ct)
    {
        if (!r.Heard || r.Seconds < 0.25)
            return new SpeechCheck(false, 0, "", "Ничего не услышано. Говори громче или открой «Проверка микрофона» в разделе «Говорение».");
        var (text, conf) = await Asr.TranscribeAsync(r.Wav, ct);
        return Speech.Score(text, conf, jp, kana);
    }

    /// <summary>Сравнение интонации и ритма с образцом (тот же голос, что звучит в приложении).</summary>
    public static async Task<ProsodyResult?> AnalyzeProsody(string jp, Recording rec, bool? male = null)
    {
        var path = await Tts.GetAsync(jp, false, male);
        if (path == null) return null;
        return await Task.Run(() => Prosody.Compare(Prosody.DecodeFile(path), Prosody.FromWav(rec.Wav)));
    }

    /// <summary>Итог: звуки 60% + интонация 25% + ритм 15% (если фраза в целом распознана).</summary>
    public static int Combine(int text, ProsodyResult? pr) =>
        pr == null || text < 40 ? text : (int)Math.Round(text * 0.6 + pr.Intonation * 0.25 + pr.Rhythm * 0.15);

    public static FrameworkElement ProsodyPanel(int text, ProsodyResult pr)
    {
        var sp = new StackPanel();
        var pills = new WrapPanel();
        foreach (var (label, v) in new[] { ("Звуки", text), ("Интонация", pr.Intonation), ("Ритм", pr.Rhythm) })
        {
            var b = U.Pill($"{label} {v}", U.B(v >= 75 ? "GoodSoft" : v >= 50 ? "AccentSoft" : "BadSoft"), U.B(v >= 75 ? "Good" : v >= 50 ? "Gold" : "Bad"));
            b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(10, 4, 10, 4);
            pills.Children.Add(b);
        }
        sp.Children.Add(pills);
        var legend = U.Row(14,
            U.Row(6, new Border { Width = 14, Height = 3, Background = U.B("Accent"), CornerRadius = new CornerRadius(2) }.CenterV(), U.T("носитель", "Muted", 12)),
            U.Row(6, new Border { Width = 14, Height = 3, Background = U.B("Info"), CornerRadius = new CornerRadius(2) }.CenterV(), U.T("ты", "Muted", 12)));
        sp.Children.Add(U.T("МЕЛОДИЯ ГОЛОСА", "Overline").M(0, 14, 0, 4));
        sp.Children.Add(legend);
        sp.Children.Add(new Controls.PitchView(pr.RefPitch, pr.UserPitch).M(0, 6, 0, 0));
        foreach (var tip in pr.Tips.Take(3))
            sp.Children.Add(U.Row(8, new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B("Gold"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) },
                U.T(tip, "Body", 13).Also(t => t.MaxWidth = 640)).M(0, 8, 0, 0));
        if (pr.Tips.Count == 0 && pr.Intonation >= 75) sp.Children.Add(U.T("Интонация и ритм очень близки к носителю.", "Body", 13, U.B("Good")).M(0, 8, 0, 0));
        return sp;
    }

    /// <summary>Карточка «распознавание речи»: статус модели Whisper и кнопка скачивания с прогрессом.</summary>
    public static Border ModelCard(Action? onReady = null)
    {
        var sp = new StackPanel();
        var card = U.Card(sp);
        void Render()
        {
            sp.Children.Clear();
            var head = U.Cols("auto", "*");
            head.Children.Add(new TextBlock { Text = "", FontFamily = U.R<FontFamily>("Icons"), FontSize = 18, Foreground = U.B(Asr.Ready ? "Good" : "Accent"), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(U.Stack(
                U.T(Asr.Ready ? "Проверка произношения готова" : "Проверка произношения", "H3"),
                U.T(Asr.Ready ? $"Модель Whisper ({Asr.Models.First(m => m.id == Asr.ModelId).title.ToLower()}) работает офлайн на этом компьютере."
                              : "Нужна бесплатная модель распознавания OpenAI Whisper. Скачивается один раз и дальше работает без интернета.", "Muted")).M(14, 0, 0, 0).Col(1));
            sp.Children.Add(head);
            if (!Recorder.HasMicrophone)
                sp.Children.Add(U.T("⚠ Микрофон не найден — подключи его, чтобы проверять произношение.", "Body", 13, U.B("Gold")).M(0, 10, 0, 0));
            if (Asr.Ready) return;

            var choose = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var m in Asr.Models)
            {
                var rb = new RadioButton { Style = U.St("ChipRadio"), GroupName = "asr" + card.GetHashCode(), IsChecked = Asr.ModelId == m.id, Content = $"{m.title} · {m.size / 1_000_000} МБ", ToolTip = m.desc };
                var id = m.id;
                rb.Checked += (_, _) => { ProgressStore.P.AsrModel = id; ProgressStore.Notify(); };
                choose.Children.Add(rb);
            }
            sp.Children.Add(choose);
            var bar = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
            var status = U.T("", "Muted").M(0, 6, 0, 0);
            var btn = U.Btn("Скачать модель", () => { }, "Primary", "");
            btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 14, 0, 0);
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false; choose.IsEnabled = false; bar.Visibility = Visibility.Visible;
                var id = Asr.ModelId; long size = Asr.Models.First(x => x.id == id).size;
                var prog = new Progress<double>(v => { bar.Value = v; status.Text = $"Скачано {v * size / 1_000_000:0} из {size / 1_000_000} МБ"; });
                try
                {
                    await Asr.DownloadAsync(id, prog, CancellationToken.None);
                    Shell.Toast("Модель распознавания установлена", "完了");
                    Asr.Warmup();
                    Render();
                    onReady?.Invoke();
                }
                catch (Exception ex)
                {
                    status.Text = "Ошибка загрузки: " + ex.Message;
                    btn.IsEnabled = true; choose.IsEnabled = true;
                }
            };
            sp.Children.Add(btn);
            sp.Children.Add(bar);
            sp.Children.Add(status);
        }
        Render();
        return card;
    }

    /// <summary>Живой индикатор громкости: 7 столбиков, обновляются 30 раз в секунду только во время записи.</summary>
    public sealed class LevelMeter : StackPanel
    {
        readonly ScaleTransform[] _bars = new ScaleTransform[7];
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        Recorder? _rec;
        readonly double[] _shape = { 0.45, 0.7, 0.9, 1, 0.9, 0.7, 0.45 };
        double _smooth;

        public LevelMeter()
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Center;
            Height = 36;
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i] = new ScaleTransform(1, 0.12);
                Children.Add(new Border
                {
                    Width = 4, Height = 36, Margin = new Thickness(2.5, 0, 2.5, 0), CornerRadius = new CornerRadius(2),
                    Background = U.B("Accent"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _bars[i],
                });
            }
            _timer.Tick += (_, _) =>
            {
                double lvl = _rec?.Level ?? 0;
                _smooth = _smooth * 0.55 + lvl * 0.45;
                for (int i = 0; i < _bars.Length; i++)
                    _bars[i].ScaleY = 0.12 + 0.88 * Math.Min(1, _smooth * _shape[i] * (0.85 + 0.3 * Math.Sin(Environment.TickCount64 / 90.0 + i)));
            };
            Opacity = 0.35;
        }

        public void Attach(Recorder? rec)
        {
            _rec = rec;
            if (rec != null) { _timer.Start(); Opacity = 1; }
            else { _timer.Stop(); Opacity = 0.35; foreach (var b in _bars) b.ScaleY = 0.12; }
        }
    }
}
