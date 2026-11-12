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

public sealed partial class SettingsPage : UserControl, IPage
{
    public string Title => _only != null ? Tabs.First(x => x.key == _only).title : "Настройки";
    bool _confirmReset;

    // Телефон: список разделов — отдельный экран, каждый раздел открывается своей страницей
    // (иначе содержимое оказывалось под длинным списком и его не было видно)
    readonly string? _only;
    public SettingsPage() { }
    public SettingsPage(string only) { _only = only; _tab = only; }

    internal static string _tab = "learn";

    static readonly (string key, string icon, string title, string sub)[] Tabs =
    {
        ("learn", "target", "Обучение", "Цель, темп, говорение"),
        ("voice", "voice", "Голос и озвучка", "Голоса, VOICEVOX"),
        ("mic", "mic", "Микрофон", "Проверка, распознавание"),
        ("look", "palette", "Оформление", "Тема, цвет, масштаб"),
        ("notify", "bell", "Уведомления", "Напоминания, слово дня"),
        ("data", "storage", "Данные", "Копия, место, обновления"),
        ("awards", "trophy", "Достижения", "Награды за учёбу"),
        ("about", "info", "О программе", "Отзывы, лицензии"),
    };

    public void OnShown()
    {
        if (_only != null)
        {
            var one = new StackPanel();
            foreach (var c in Build(_only)) one.Children.Add(c);
            Content = U.Page(one);
            Anim.Stagger(one, 40);
            return;
        }
        var root = new StackPanel { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(U.Header("設", "Настройки", "Всё, что можно настроить под себя — по разделам"));

        var tabs = new StackPanel();
        foreach (var (key, icon, title, sub) in Tabs)
        {
            bool on = key == _tab && !Shell.Compact;
            var g = U.Cols("auto", "*");
            g.Children.Add(Art.Icon(icon, 34).CenterV());
            g.Children.Add(U.Stack(U.T(title, "H3", 14, U.B(on ? "Text" : "Muted")), U.T(sub, "Muted", 11.5)).CenterV().M(12, 0, 0, 0).Col(1));
            var item = new Border
            {
                Child = g, Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
                Background = on ? U.B("AccentSoft") : Brushes.Transparent, BorderBrush = on ? U.B("Stroke") : Brushes.Transparent, BorderThickness = new Thickness(1),
            };
            var k = key;
            item.PointerEntered += (_, _) => { if (k != _tab) item.Background = U.B("SelectedTint"); };
            item.PointerExited += (_, _) => { if (k != _tab) item.Background = Brushes.Transparent; };
            item.PointerReleased += (_, _) => { if (Shell.Compact) Shell.Open(new SettingsPage(k)); else { _tab = k; OnShown(); } };
            tabs.Children.Add(item);
        }

        if (Shell.Compact) { Content = U.Page(root.Also(r => r.Children.Add(tabs))); return; }
        var body = new StackPanel();
        foreach (var c in Build(_tab)) body.Children.Add(c);
        var layout = U.Cols("250", "28", "*");
        layout.Children.Add(tabs);
        layout.Children.Add(body.Col(2));
        root.Children.Add(U.Stackify(layout));
        Content = U.Page(root);
        Anim.Stagger(body, 40);
    }

    IEnumerable<Control> Build(string tab)
    {
        var p = ProgressStore.P;
        switch (tab)
        {
            case "learn":
                yield return Section("Дневная цель", "Сколько XP набирать в день. 50 XP ≈ 15 минут занятий.",
                    Choice(new[] { 20, 50, 100, 150 }, p.DailyGoal, v => p.DailyGoal = v, v => $"{v} XP"));
                yield return Section("Новых слов за раз", "Сколько новых слов показывать в одном уроке-знакомстве.",
                    Choice(new[] { 5, 10, 15, 20 }, p.NewPerSession, v => p.NewPerSession = v, v => v.ToString()));
                yield return Section("Проверка произношения", "Лёгкая — как в Duolingo: засчитывается, если фразу можно понять. Строгая — для отработки акцента.",
                    U.Stack(ChoiceS(new[] { "easy", "normal", "strict" }, p.SpeechMode, v => { p.SpeechMode = v; OnShown(); }, v => v switch { "easy" => "Лёгкая", "normal" => "Обычная", _ => "Строгая" }),
                        U.T(p.SpeechMode switch
                        {
                            "easy" => "Порог зачёта 55 из 100. Интонация не снижает балл, подробный разбор — по кнопке.",
                            "normal" => "Порог зачёта 70. Учитываются звуки, интонация и ритм.",
                            _ => "Порог зачёта 82. Для тех, кто хочет звучать как носитель.",
                        }, "Muted", 12.5).M(0, 10, 0, 0)));
                yield return Section("Подсказки на карточках", null, U.Stack(
                    Toggle("Показывать ромадзи (латиницу)", p.ShowRomaji, v => p.ShowRomaji = v),
                    Toggle("Автоматически озвучивать слова", p.AutoSpeak, v => p.AutoSpeak = v)));
                break;

            case "voice":
                yield return Section("Движок озвучки", "Авто: офлайн-голос VOICEVOX, если установлен, иначе нейро-голос онлайн.",
                    ChoiceS(new[] { "auto", "online", "offline" }, p.TtsEngine, v => { p.TtsEngine = v; OnShown(); }, v => v switch { "auto" => "Авто", "online" => "Онлайн (Microsoft)", _ => "Офлайн (VOICEVOX)" }));
                yield return Section("Голос онлайн", "Нейросетевая озвучка Microsoft Neural. Каждая фраза сохраняется на диск — повторно звучит мгновенно и без интернета.", VoiceBlock());
                yield return Section("Офлайн-голоса VOICEVOX", "Бесплатный японский синтез без интернета. Отобраны чёткие голоса с ровной дикцией, а звук чистится: без гула, щелчков и придыхания. При публикации озвучки указывай «VOICEVOX:имя персонажа».", VoicevoxBlock());
                break;

            case "mic":
                yield return Section("Микрофон", $"Режим: {(p.MicMode == "auto" ? $"авто-стоп после паузы {p.MicPause:0.0} с" : "ручная остановка")}.",
                    U.Btn("Проверка и настройка микрофона", () => Shell.Open(new MicCheckPage()), "", "").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
                yield return SpeechUi.ModelCard(OnShown).M(0, 0, 0, 16);
                if (Asr.Ready)
                    yield return Section("Модель распознавания", "Точная лучше слышит ошибки, быстрая — для слабых компьютеров. Переключение скачает выбранную модель.",
                        Choice(new[] { 0, 1 }, Asr.ModelId == Asr.Models[0].id ? 0 : 1, v => { ProgressStore.P.AsrModel = Asr.Models[v].id; Asr.Unload(); OnShown(); }, v => Asr.Models[v].title));
                break;

            case "look":
                yield return Section("Тема", "Применяется после перезапуска.",
                    Choice(new[] { 0, 2, 1 }, p.LightTheme ? 1 : p.NightTheme ? 2 : 0, v => { p.LightTheme = v == 1; p.NightTheme = v == 2; AskRestart(); }, v => v switch { 0 => "Тёмная", 1 => "Светлая", _ => "Ночь" }));
                var fonts = Enumerable.Range(0, App.JpFonts.Length).Where(i => App.FontInstalled(App.JpFonts[i].family)).ToArray();
                yield return Section("Шрифт японского текста", "«Учебник» — как в японских школьных прописях: формы знаков ближе к рукописным. Применяется после перезапуска.", U.Stack(
                    Choice(fonts, Math.Max(0, Array.FindIndex(App.JpFonts, f => f.name == p.JpFontName)), v => { p.JpFontName = App.JpFonts[v].name; AskRestart(); }, v => App.JpFonts[v].title),
                    U.Row(18, fonts.Select(i => (Control)new TextBlock
                    {
                        Text = "日本語 あア 字", FontSize = 20, Foreground = U.B("Text"),
                        FontFamily = App.JpFonts[i].family.Length > 0 ? new FontFamily(App.JpFonts[i].family) : U.R<FontFamily>("JpFont"),
                    }.Tip(App.JpFonts[i].title)).ToArray()).M(0, 12, 0, 0)));
                yield return Section("Акцентный цвет", null, Swatches());
                yield return Section("Масштаб интерфейса", null,
                    Choice(new[] { 90, 100, 115, 130 }, (int)Math.Round(p.UiScale * 100), v => { p.UiScale = v / 100.0; AskRestart(); }, v => v + "%"));
                yield return Section("Анимация и звук", null, U.Stack(
                    Toggle("Анимированное интро при запуске", p.Intro, v => p.Intro = v),
                    Toggle("Звуки ответов", p.Sounds, v => { p.Sounds = v; if (v) Sfx.Ok(); })));
                break;

            case "notify":
                yield return Section("Напоминания", null, U.Stack(
                    Toggle("Системные напоминания (повторение, серия)", p.Reminders, v => p.Reminders = v),
                    Toggle("Мягкий звук уведомлений", p.NotifySound, v => { p.NotifySound = v; if (v) Sfx.Reminder(); }),
                    Toggle("Закрытие окна сворачивает в трей (напоминания продолжают приходить)", p.MinimizeToTray, v => p.MinimizeToTray = v).DesktopOnly()));
                yield return Section("Звук уведомлений", "Нажми, чтобы выбрать и послушать. Все звуки тихие и короткие.",
                    Choice(Enumerable.Range(0, Sfx.NotifyTones.Length).ToArray(), Math.Max(0, Array.FindIndex(Sfx.NotifyTones, t => t.id == p.NotifyTone)),
                        v => { p.NotifyTone = Sfx.NotifyTones[v].id; p.NotifySound = true; ProgressStore.Notify(); Sfx.Reminder(); }, v => Sfx.NotifyTones[v].title));
                var wod = new WrapPanel();
                foreach (var (t, label) in new[] { ("", "Выключено"), ("08:00", "08:00"), ("09:00", "09:00"), ("12:00", "12:00"), ("19:00", "19:00"), ("21:00", "21:00") })
                {
                    var rb = new RadioButton { Classes = { "ChipRadio" }, Content = label, GroupName = "wod", IsChecked = p.WordOfDayTime == t, Margin = new Thickness(0, 0, 8, 8) };
                    var tt = t;
                    rb.Checked += (_, _) => { p.WordOfDayTime = tt; ProgressStore.Notify(); };
                    wod.Children.Add(rb);
                }
                yield return Section("Слово дня", "Каждый день в выбранное время — уведомление со словом, примером и озвучкой. Программа должна быть запущена (можно свёрнутой в трей).",
                    U.Stack(wod, Toggle("Произносить слово и пример вслух", p.WordOfDaySpeak, v => p.WordOfDaySpeak = v).M(0, 4, 0, 0),
                        U.Btn("Показать слово дня", () => WordOfDay.Show(force: true), "", "").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 8, 0, 0); })));
                break;

            case "data":
                yield return Section("Резервная копия", "Сохрани прогресс в файл — пригодится при переустановке или переносе на другой компьютер.", U.Row(10,
                    U.Btn("Экспорт прогресса", ExportProgress, "", ""),
                    U.Btn("Импорт из файла", ImportProgress, "", "")));
                yield return Section("Обновления", $"Установлена версия {Updater.Current.ToString(3)}. Новые версии берутся из github.com/{Updater.Repo}.", UpdateBlock());
                yield return Section("Хранилище", $"Всего занято: {Storage.Format(Storage.Total)} · папка: {ProgressStore.Folder}", StorageBlock());
                var reset = U.Btn("Сбросить весь прогресс", () => { }, "", "");
                reset.HorizontalAlignment = HorizontalAlignment.Left;
                reset.Click += (_, _) =>
                {
                    if (!_confirmReset) { _confirmReset = true; ((TextBlock)((StackPanel)reset.Content!).Children[1]).Text = "Точно? Нажми ещё раз — это необратимо"; reset.BorderBrush = U.B("Bad"); return; }
                    ProgressStore.Reset();
                    _confirmReset = false;
                    Shell.Toast("Прогресс сброшен. Начинаем с чистого листа!", "新");
                    OnShown();
                };
                yield return Section("Сброс", $"Прогресс хранится локально: {ProgressStore.Folder}", reset);
                break;

            case "awards":
                yield return Section("Достижения", $"Открыто {p.Achievements.Count} из {Achievements.All.Length}", AchievementsGrid());
                break;

            default:
                var report = U.Row(10,
                    U.Btn("Сообщить об ошибке", () => CrashReport.Open(), "", ""),
                    U.Btn("Предложить идею", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://github.com/{Updater.Repo}/issues/new?labels=enhancement") { UseShellExecute = true }); } catch { } }, "Ghost", ""));
                yield return U.Card(Art.Say($"Kotodama {Updater.Current.ToString(3)} · 言霊 — «душа слова»: правильно произнесённое слово обретает силу. Я Википе-тан — буду помогать тебе учиться!", Mood.Joy, 120)).M(0, 0, 0, 16);
                yield return Section("Обратная связь", CrashReport.LastEntry() is { } ? "В журнале есть ошибка — отчёт откроется на GitHub уже заполненным, ты только нажмёшь «Submit»." : "Нашёл ошибку или есть идея? Откроется форма на GitHub.", report);
                yield return Section("Открытые данные", null, U.Stack(
                    U.T("Словарь: JMdict © EDRDG, лицензия CC BY-SA 4.0 (сборка jmdict-simplified).", "Muted"),
                    U.T("Порядок черт: KanjiVG © Ulrich Apel, лицензия CC BY-SA 3.0.", "Muted"),
                    U.T("Питч-акцент: Kanjium (CC BY-SA 4.0). Части кандзи: KRADFILE, KANJIDIC2 © EDRDG (CC BY-SA 4.0).", "Muted"),
                    U.T("Распознавание речи: OpenAI Whisper (MIT) через whisper.cpp / Whisper.net.", "Muted"),
                    U.T("Иллюстрации: Microsoft Fluent Emoji (MIT); иконки — собственные.", "Muted"),
                    U.T("Аниме-маскот Википе-тан: Wikimedia Commons, CC BY-SA 3.0/4.0 — художники Kasuga, David Revoy, 燃灯, Di, SunRuikang, Tommyang.", "Muted"),
                    U.T("Офлайн-синтез: VOICEVOX (голоса — по условиям персонажей). Примеры фраз: Tatoeba (CC BY 2.0 FR).", "Muted"),
                    U.T("Морфологический разбор при подготовке латиницы: MeCab + UniDic.", "Muted")));
                break;
        }
    }

    /// <summary>Акцентный цвет — круглыми образцами вместо списка названий.</summary>
    Control Swatches()
    {
        var p = ProgressStore.P;
        var row = new WrapPanel();
        foreach (var (key, name, hex) in new[] { ("sakura", "Сакура", "#F07AA0"), ("mint", "Мята", "#3DD6A8"), ("indigo", "Индиго", "#7C83FF"), ("amber", "Янтарь", "#F5B240") })
        {
            bool on = p.AccentColor == key;
            var dot = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(Color.Parse(hex)), Cursor = Cursors.Hand,
                BorderBrush = on ? U.B("Text") : Brushes.Transparent, BorderThickness = new Thickness(3),
                Child = on ? new TextBlock { Text = "✓", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
            };
            var k = key;
            dot.PointerReleased += (_, _) => { if (p.AccentColor == k) return; p.AccentColor = k; ProgressStore.Notify(); OnShown(); AskRestart(); };
            row.Children.Add(U.Stack(dot.Center(), U.T(name, "Muted", 12).Center().M(0, 6, 0, 0)).M(0, 0, 18, 0));
        }
        return row;
    }

    static async void AskRestart()
    {
        ProgressStore.Save();
        if (await Shell.Confirm("Изменение применится после перезапуска. Перезапустить сейчас?", "Перезапустить"))
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    static WrapPanel ChoiceS(string[] values, string current, Action<string> set, Func<string, string> label)
    {
        var sp = new WrapPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var v in values)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = label(v), GroupName = group, IsChecked = v == current };
            rb.Checked += (_, _) => { set(v); ProgressStore.Notify(); };
            sp.Children.Add(rb);
        }
        return sp;
    }

    static readonly Avalonia.Platform.Storage.FilePickerFileType JsonType = new("Прогресс Kotodama") { Patterns = new[] { "*.json" } };

    static async void ExportProgress()
    {
        var sp = TopLevel.GetTopLevel(Shell.Main)?.StorageProvider;
        if (sp == null) return;
        var file = await sp.SaveFilePickerAsync(new() { SuggestedFileName = $"kotodama-{DateTime.Today:yyyy-MM-dd}.json", FileTypeChoices = new[] { JsonType }, DefaultExtension = "json" });
        if (file == null) return;
        ProgressStore.Save();
        await using (var dst = await file.OpenWriteAsync())
        await using (var src = System.IO.File.OpenRead(System.IO.Path.Combine(ProgressStore.Folder, "progress.json")))
            await src.CopyToAsync(dst);
        Shell.Toast("Прогресс сохранён в файл", "保存");
    }

    static async void ImportProgress()
    {
        var sp = TopLevel.GetTopLevel(Shell.Main)?.StorageProvider;
        if (sp == null) return;
        var files = await sp.OpenFilePickerAsync(new() { FileTypeFilter = new[] { JsonType }, AllowMultiple = false });
        if (files.Count == 0) return;
        var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]);
        if (path == null || !ProgressStore.Import(path)) { Shell.Toast("Файл не похож на прогресс Kotodama", "!"); return; }
        Shell.Toast("Прогресс восстановлен", "復元");
        Shell.Go("home");
    }

    public static Control AchievementsGrid()
    {
        var p = ProgressStore.P;
        var wrap = new WrapPanel();
        foreach (var a in Achievements.All)
        {
            bool got = p.Achievements.Contains(a.Id);
            var sp = U.Stack(U.Jp(a.Jp, 22, U.B(got ? "Accent" : "Faint"), true).Center(), U.T(a.Title, "H3", 12.5, U.B(got ? "Text" : "Faint")).Center().M(0, 4, 0, 0), U.T(a.Desc, "Muted", 11).Center());
            wrap.Children.Add(new Border { Width = 132, Height = 112, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Padding = new Thickness(8),
                Background = U.B(got ? "AccentSoft" : "Surface2"), Child = sp.CenterV(), Opacity = got ? 1 : 0.6 });
        }
        return wrap;
    }

    Control VoiceBlock()
    {
        var p = ProgressStore.P;
        var sp = new StackPanel();
        var voices = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (male, name) in new[] { (false, "Nanami · женский"), (true, "Keita · мужской") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "voice", IsChecked = p.MaleVoice == male };
            rb.Checked += (_, _) => { p.MaleVoice = male; ProgressStore.Notify(); Speech.Say("こんにちは、よろしくお願いします。"); };
            voices.Children.Add(rb);
        }
        voices.Children.Add(U.Btn("Проверить", () => Speech.Say("日本語の勉強、頑張りましょう！"), "Ghost", ""));
        sp.Children.Add(voices);

        var info = U.T($"Кэш озвучки: {Tts.CacheSizeBytes() / 1_048_576.0:0.0} МБ", "Muted").M(0, 14, 0, 6);
        var bar = new ProgressBar { Maximum = 1, IsVisible = false, Margin = new Thickness(0, 4, 0, 4) };
        var btn = U.Btn("Скачать озвучку всего курса (офлайн)", () => { }, "", "");
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false; bar.IsVisible = true;
            var texts = Db.Words.Select(w => w.Kana)
                .Concat(Db.SpeechLessons.SelectMany(l => l.Items).Select(i => i.Jp))
                .Concat(Db.Grammar.SelectMany(g => g.Points).SelectMany(pt => pt.Examples).Select(e => e.Jp))
                .Concat(Db.Course.SelectMany(u => u.Examples).Select(e => e.Jp))
                .Concat(Db.Kanji.SelectMany(k => k.Examples).Select(e => e.Kana))
                .Distinct().ToList();
            var prog = new Progress<(int done, int total)>(v => { bar.Value = (double)v.done / v.total; info.Text = $"Озвучено {v.done} из {v.total}…"; });
            int ok = await Tts.PrefetchAsync(texts, prog, CancellationToken.None);
            // Голоса для диалогов (свой голос у каждого персонажа)
            foreach (var d in Db.Dialogs) foreach (var l in d.Lines) await Tts.GetAsync(l.Jp, false, d.Male[l.Speaker]);
            info.Text = ok == texts.Count ? $"Готово! Весь курс озвучен ({Tts.CacheSizeBytes() / 1_048_576.0:0.0} МБ) — работает без интернета."
                                          : $"Озвучено {ok} из {texts.Count}. Остальное докачается при следующем подключении.";
            btn.IsEnabled = true; bar.IsVisible = false;
        };
        sp.Children.Add(info);
        sp.Children.Add(bar);
        sp.Children.Add(btn);
        return sp;
    }

    static Border Section(string title, string? desc, Control body)
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T(title, "H3", 16));
        if (desc != null) sp.Children.Add(U.T(desc, "Muted").M(0, 4, 0, 0));
        if (body is Control fe) fe.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(body);
        return U.Card(sp).M(0, 0, 0, 16);
    }

    static WrapPanel Choice(int[] values, int current, Action<int> set, Func<int, string> label)
    {
        var sp = new WrapPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var v in values)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = label(v), GroupName = group, IsChecked = v == current };
            rb.Checked += (_, _) => { set(v); ProgressStore.Notify(); };
            sp.Children.Add(rb);
        }
        return sp;
    }

    static Control Toggle(string text, bool on, Action<bool> set) => U.Switch(text, on, v => { set(v); ProgressStore.Notify(); });
}
