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

namespace Matsubakira.Views;

/// <summary>Проверка микрофона: выбор устройства, режим записи, живой уровень, пробная запись с разбором качества.</summary>
public sealed class MicCheckPage : UserControl, IPage
{
    public string Title => "Проверка микрофона";
    const string TestJp = "こんにちは、よろしくお願いします。", TestKana = "こんにちは、よろしくおねがいします。";
    readonly SpeechUi.LevelMeter _meter = new();
    readonly TextBlock _status = U.T("", "Body", 14);
    readonly ContentControl _result = new();
    Recorder? _rec;
    Recording? _last;
    bool _busy;

    public MicCheckPage() => Unloaded += (_, _) => _rec?.Stop();

    public void OnShown()
    {
        var p = ProgressStore.P;
        var root = new StackPanel { MaxWidth = 860 };
        root.Children.Add(U.Header("耳", "Проверка микрофона", "Настрой микрофон один раз — и распознавание будет работать стабильно."));

        // Устройство
        var devs = Recorder.Devices();
        var devPanel = new WrapPanel();
        if (!OperatingSystem.IsWindows())
            devPanel.Children.Add(U.T(Recorder.HasMicrophone
                ? "Используется микрофон по умолчанию из настроек звука системы (PulseAudio / PipeWire). Сменить его можно в настройках звука."
                : "Не найдена программа записи звука. Установи пакет pulseaudio-utils (parecord), pipewire (pw-record) или alsa-utils (arecord).", "Body", 14, Recorder.HasMicrophone ? null : U.B("Bad")));
        else if (devs.Count == 0) devPanel.Children.Add(U.T("Микрофон не найден. Подключи его и открой страницу снова.", "Body", 14, U.B("Bad")));
        if (p.MicDevice >= devs.Count) p.MicDevice = -1;
        for (int i = devs.Count > 0 ? -1 : 0; i < devs.Count; i++)
        {
            int idx = i;
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = i < 0 ? "Как в Windows (по умолчанию)" : devs[i], GroupName = "mic", IsChecked = p.MicDevice == i, Margin = new Thickness(0, 0, 8, 8) };
            rb.Checked += (_, _) => { p.MicDevice = idx; ProgressStore.Notify(); };
            devPanel.Children.Add(rb);
        }
        root.Children.Add(Section("Устройство", devPanel));

        // Режим
        var mode = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, name) in new[] { ("auto", "Авто: стоп после паузы"), ("manual", "Вручную: стоп кнопкой") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "mode", IsChecked = p.MicMode == key };
            rb.Checked += (_, _) => { p.MicMode = key; ProgressStore.Notify(); OnShown(); };
            mode.Children.Add(rb);
        }
        var modeBox = U.Stack(mode, U.T(p.MicMode == "auto"
            ? "Запись остановится сама, когда ты замолчишь. Если обрезает на паузах — увеличь паузу ниже или выбери ручной режим."
            : "Нажми на микрофон, говори сколько нужно и нажми ещё раз (или пробел), чтобы закончить.", "Muted").M(0, 8, 0, 0));
        if (p.MicMode == "auto")
        {
            var pause = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            pause.Children.Add(U.T("Пауза:", "Muted").CenterV().M(0, 0, 10, 0));
            foreach (var v in new[] { 1.0, 1.5, 2.0, 2.5 })
            {
                var rb = new RadioButton { Classes = { "ChipRadio" }, Content = $"{v:0.0} с", GroupName = "pause", IsChecked = Math.Abs(p.MicPause - v) < 0.01 };
                rb.Checked += (_, _) => { p.MicPause = v; ProgressStore.Notify(); };
                pause.Children.Add(rb);
            }
            modeBox.Children.Add(pause);
        }
        root.Children.Add(Section("Режим записи", modeBox));

        // Пробная запись
        var test = new StackPanel();
        test.Children.Add(U.T("Нажми «Записать» и скажи:", "Muted"));
        test.Children.Add(U.Row(10, U.Jp(TestJp, 22), U.SpeakBtn(() => TestJp)).M(0, 6, 0, 2));
        test.Children.Add(U.T(Kana.ToRomaji("こんにちは、 よろしく おねがい します。"), "Muted", 13));
        var row = U.Cols("auto", "*");
        row.Margin = new Thickness(0, 14, 0, 0);
        row.Children.Add(U.Btn("Записать", Record, "Primary", ""));
        row.Children.Add(_meter.Detach().CenterV().M(16, 0, 0, 0).Col(1));
        _meter.HorizontalAlignment = HorizontalAlignment.Left;
        test.Children.Add(row);
        test.Children.Add(_status.Detach().M(0, 12, 0, 0));
        test.Children.Add(_result.Detach().M(0, 8, 0, 0));
        root.Children.Add(Section("Пробная запись", test));

        if (OperatingSystem.IsWindows())
            root.Children.Add(U.Row(10,
                U.Btn("Звук Windows", () => Open("ms-settings:sound"), "", ""),
                U.Btn("Доступ к микрофону", Speech.OpenMicPrivacy, "", "")).M(0, 4, 0, 0));
        Content = U.Page(root);
    }

    static Border Section(string title, Control body) => U.Card(U.Stack(U.T(title, "H3", 15), body is Control fe ? fe.M(0, 12, 0, 0) : body)).M(0, 0, 0, 14);

    static void Open(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    async void Record()
    {
        if (_busy) { _rec?.Stop(); return; }
        if (!Recorder.HasMicrophone) { _status.Text = "Микрофон не найден."; return; }
        _busy = true;
        Speech.Stop();
        _result.Content = null;
        _rec = new Recorder(manual: false, maxSeconds: 8);
        _meter.Attach(_rec);
        _status.Text = "Включаю микрофон…";
        _rec.Started += () => Dispatcher.UIThread.Post(() => { if (_busy) _status.Text = "Говори фразу (или нажми «Записать» ещё раз, чтобы остановить)"; });
        _status.Foreground = U.B("Text");
        try
        {
            _last = await _rec.RecordAsync(CancellationToken.None);
            _meter.Attach(null);
            await Analyze(_last);
        }
        catch (Exception ex)
        {
            _status.Text = "Не удалось включить микрофон: " + ex.Message + ". Проверь доступ к микрофону в Windows.";
            _status.Foreground = U.B("Bad");
        }
        finally { _busy = false; _meter.Attach(null); _rec?.Dispose(); _rec = null; }
    }

    async Task Analyze(Recording r)
    {
        var tips = new List<(string text, string brush)>();
        if (!r.Heard) tips.Add(("Голос не обнаружен. Проверь, тот ли микрофон выбран и не выключен ли он.", "Bad"));
        else
        {
            if (r.PeakLevel < 0.05f) tips.Add(("Очень тихо: подвинь микрофон ближе или увеличь его громкость в «Звук Windows» (в Linux — в настройках звука системы). Приложение усиливает голос, но шум усиливается тоже.", "Gold"));
            else if (r.PeakLevel > 0.97f) tips.Add(("Слишком громко — есть перегруз (клиппинг). Уменьши громкость микрофона на 10–20%.", "Gold"));
            else tips.Add(("Уровень громкости в норме.", "Good"));
            if (r.NoiseLevel > 0.02f) tips.Add(("Много фонового шума — закрой окно, выключи вентилятор рядом или говори ближе к микрофону.", "Gold"));
            else tips.Add(("Фоновый шум низкий.", "Good"));
            if (r.Seconds < 1.2) tips.Add(("Запись очень короткая — возможно, фраза обрезалась. Попробуй паузу 2,0 с или ручной режим.", "Gold"));
        }
        var sp = new StackPanel();
        foreach (var (text, brush) in tips)
            sp.Children.Add(U.Row(8, new TextBlock { Text = brush == "Good" ? "" : "", FontFamily = U.R<FontFamily>("Icons"), Foreground = U.B(brush), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) },
                U.T(text, "Body", 13.5).Also(t => t.MaxWidth = 680)).M(0, 0, 0, 6));
        if (r.Heard)
        {
            var wave = new Controls.WaveView(U.B("Info"));
            wave.SetPeaks(r.Peaks, r.Seconds);
            sp.Children.Add(SpeechLessonPage.WaveRow("Запись", wave, () => _ = Speech.PlayWavAsync(r.Wav), out var sec).M(0, 8, 0, 0));
            sec.Text = $"{r.Seconds:0.0} с";
        }
        _result.Content = sp;
        _status.Text = "";

        if (r.Heard && Asr.Ready)
        {
            _status.Text = "Проверяю распознавание…";
            var sc = await SpeechUi.ScoreRecording(TestJp, TestKana, r, CancellationToken.None, s => _status.Text = s);
            _status.Text = sc.Heard.Length == 0 ? "Распознать не удалось." : $"Распознано: «{sc.Heard}» — совпадение {sc.Score}%";
            _status.Foreground = U.B(sc.Score >= 70 ? "Good" : "Gold");
            if (sc.Score >= 70) Shell.Toast("Микрофон и распознавание работают отлично", "完璧");
        }
        else if (!Asr.Ready) _status.Text = "Чтобы проверить распознавание, скачай модель Whisper в разделе «Говорение».";
    }
}
