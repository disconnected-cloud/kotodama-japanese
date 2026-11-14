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
                    Toggle("Автоматически озвучивать слова", p.AutoSpeak, v => p.AutoSpeak = v),
                    Toggle("Тихий режим: без звука и микрофона (только текстовые задания)", p.QuietMode, v => p.QuietMode = v)));
                break;

            case "voice":
                yield return Section("Голоса", "Вся озвучка работает без интернета. Порядок: VOICEVOX, затем системный голос.", VoiceBlock());
                yield return Section("Офлайн-голоса VOICEVOX", "Бесплатный японский синтез без интернета. Отобраны чёткие голоса с ровной дикцией, а звук чистится: без гула, щелчков и придыхания. У каждого персонажа свои условия: при публикации озвучки указывай «VOICEVOX:имя персонажа».", VoicevoxBlock());
                break;

            case "mic":
                yield return Section("Микрофон", $"Режим: {(p.MicMode == "auto" ? $"авто-стоп после паузы {p.MicPause:0.0} с" : "ручная остановка")}.",
                    U.Btn("Проверка и настройка микрофона", () => Shell.Open(new MicCheckPage()), "", "").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
                yield return SpeechUi.ModelCard(OnShown).M(0, 0, 0, 16);
                if (Asr.Ready)
                    yield return Section("Модель распознавания", "Максимальная лучше всех понимает речь с акцентом (нужно 8 ГБ памяти), быстрая — для слабых компьютеров. Переключение скачает выбранную модель. Модели обучены на речи носителей, поэтому оценка акцента приблизительная.",
                        Choice(Enumerable.Range(0, Asr.Models.Length).ToArray(), Math.Max(0, Array.FindIndex(Asr.Models, m => m.id == Asr.ModelId)), v => { ProgressStore.P.AsrModel = Asr.Models[v].id; Task.Run(Asr.Unload); OnShown(); }, v => Asr.Models[v].title));
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
                yield return Section("Размер японского текста", "Иероглифы и кана — крупнее или мельче, отдельно от остального интерфейса.",
                    Choice(new[] { 90, 100, 115, 130, 145 }, (int)Math.Round(p.JpScale * 100), v => { p.JpScale = v / 100.0; ProgressStore.Notify(); OnShown(); }, v => v + "%"));
                yield return Section("Высокий контраст", "Ярче текст, подписи и границы — легче читать на солнце и при слабом зрении. Применяется после перезапуска.",
                    Toggle("Включить высокий контраст", p.HighContrast, v => { p.HighContrast = v; AskRestart(); }));
                yield return Section("Акцентный цвет", null, Swatches());
                yield return Section("Масштаб интерфейса", null,
                    Choice(new[] { 90, 100, 115, 130 }, (int)Math.Round(p.UiScale * 100), v => { p.UiScale = v / 100.0; AskRestart(); }, v => v + "%"));
                yield return Section("Плавность", Anim.LowEndDevice ? "На этом телефоне анимации появления по умолчанию выключены — так быстрее." : "Без анимаций экраны открываются мгновенно — удобно на слабом телефоне.",
                    Choice(new[] { 0, 1, 2 }, p.Motion, v => { p.Motion = v; Anim.ApplyMotion(v); ProgressStore.Notify(); }, v => v switch { 0 => "Авто", 1 => "С анимацией", _ => "Без анимаций" }));
                yield return Section("Анимация и звук", null, U.Stack(
                    Toggle("Анимированное интро при запуске", p.Intro, v => p.Intro = v),
                    Toggle("Звуки ответов", p.Sounds, v => { p.Sounds = v; if (v) Sfx.Ok(); })));
                break;

            case "notify":
                yield return Section("Напоминания", null, U.Stack(
                    Toggle(Shell.Compact ? "Напоминание в 19:00, если сегодня ещё не занимался" : "Системные напоминания (повторение, серия)", p.Reminders, v => p.Reminders = v),
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
                yield return Section("Экспорт в Anki", "Карточки для Anki: слово, чтение, латиница, перевод, пример и твоя заметка. В Anki: Файл → Импорт.", U.Row(10,
                    U.Btn("Изучаемые", () => ExportAnki(AnkiExport.Set.Learning), "", ""),
                    U.Btn($"Весь уровень {p.Level}", () => ExportAnki(AnkiExport.Set.Level), "", ""),
                    U.Btn("Тетрадь ошибок", () => ExportAnki(AnkiExport.Set.Mistakes), "", "")));
                yield return Section("Автокопии", "Каждый день при запуске прогресс копируется сам — хранятся последние 14 дней. Если что-то пошло не так, можно вернуться к копии.", AutoBackupBlock());
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
                    U.T(Voicevox.Credits() is { Length: > 0 } cr ? "Озвучка: " + cr : "Голоса VOICEVOX не установлены.", "Muted"),
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
            System.Diagnostics.Process.Start(Environment.ProcessPath!, Program.RestartArg);
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

    static async void ExportAnki(AnkiExport.Set set)
    {
        var (text, count) = AnkiExport.Build(set);
        if (count == 0) { Shell.Toast(set == AnkiExport.Set.Learning ? "Пока нет изучаемых слов — начни с раздела «Слова»" : "Нечего экспортировать", "空"); return; }
        var sp = TopLevel.GetTopLevel(Shell.Main)?.StorageProvider;
        if (sp == null) return;
        var name = set switch { AnkiExport.Set.Level => "уровень-" + ProgressStore.P.Level, AnkiExport.Set.Mistakes => "ошибки", _ => "изучаемые" };
        var file = await sp.SaveFilePickerAsync(new()
        {
            SuggestedFileName = $"kotodama-anki-{name}.txt", DefaultExtension = "txt",
            FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Текст для Anki") { Patterns = new[] { "*.txt" } } },
        });
        if (file == null) return;
        await using (var dst = await file.OpenWriteAsync())
        await using (var w = new System.IO.StreamWriter(dst, new System.Text.UTF8Encoding(false)))
            await w.WriteAsync(text);
        Shell.Toast($"Сохранено карточек: {count}. В Anki: Файл → Импорт", "暗");
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
            var sp = U.Stack(U.Jp(a.Jp, 22, U.B(got ? "Accent" : "Faint"), true).Center(), U.T(a.Title, "H3", 12.5, U.B(got ? "Text" : "Faint")).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; }).Center().M(0, 4, 0, 0),
                U.T(a.Desc, "Muted", 11).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; }).Center());
            wrap.Children.Add(new Border { Width = 132, MinHeight = 112, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Padding = new Thickness(8),
                Background = U.B(got ? "AccentSoft" : "Surface2"), Child = sp.CenterV(), Opacity = got ? 1 : 0.6 });
        }
        return wrap;
    }

    CancellationTokenSource? _prefetchCts;

    /// <summary>Экран «Голоса»: что установлено, сколько занимает, «Прослушать» и «Удалить»; озвучка курса заранее.</summary>
    Control VoiceBlock()
    {
        var p = ProgressStore.P;
        var sp = new StackPanel();
        Control Row(string title, string state, bool ok, params Control[] buttons)
        {
            var g = U.Cols("*", "auto");
            g.Children.Add(U.Stack(U.T(title, "Body", 14), U.T(state, "Muted", 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap)).Also(x => x.Margin = new Thickness(0, 0, 12, 0)));
            var btns = U.Row(8, buttons).Col(1);
            btns.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(btns);
            return new Border { Child = g, Padding = new Thickness(0, 8), BorderBrush = U.B(ok ? "Good" : "Line"), BorderThickness = new Thickness(3, 0, 0, 0) }.Also(b => b.Padding = new Thickness(12, 8, 0, 8)).M(0, 0, 0, 8);
        }
        var vvItem = Storage.Items.First(i => i.Key == "voicevox");
        var cacheItem = Storage.Items.First(i => i.Key == "audio");
        sp.Children.Add(Row("VOICEVOX — основной японский голос",
            Voicevox.Installed ? $"Установлен · {Storage.Format(Storage.Size(vvItem))} · {VoiceNames.Name(Voicevox.InstalledStyles().FirstOrDefault(s => s.Id == Voicevox.PickStyle(false))?.Character ?? "")}"
            : Voicevox.Downloaded ? "Скачан, но проверка синтеза не пройдена — ниже кнопка проверки" : "Не установлен — установить можно ниже",
            Voicevox.Installed,
            U.Btn("Прослушать", () => Speech.Say("こんにちは、一緒に日本語を勉強しましょう。"), "Ghost", "").Also(b => b.IsEnabled = Voicevox.Installed),
            U.Btn("Удалить", async () =>
            {
                if (!await Shell.Confirm($"Удалить VOICEVOX ({Storage.Format(Storage.Size(vvItem))})? Озвучка перейдёт на системный голос.", "Удалить")) return;
                Shell.Toast(Storage.Clear(vvItem) ? "VOICEVOX удалён" : "Файлы заняты — удалятся при следующем запуске", "削");
                OnShown();
            }, "Ghost", "").Also(b => b.IsEnabled = Voicevox.Downloaded)));
        sp.Children.Add(Row("Системный японский голос — запасной",
            Platform.SystemVoice.JaName is { Length: > 0 } ja ? ja : "Нет. " + Platform.SystemVoice.HowToInstallJa,
            Platform.SystemVoice.HasJa));
        sp.Children.Add(Row("Русский голос — перевод в аудиоплеере",
            Platform.SystemVoice.RuName is { Length: > 0 } ru ? ru : OperatingSystem.IsWindows()
                ? "Нет. Параметры → Время и язык → Речь → Добавить голоса → Русский. Без голоса плеер показывает перевод текстом."
                : "Нет. Установи RHVoice или espeak-ng. Без голоса плеер показывает перевод текстом.",
            Platform.SystemVoice.HasRu,
            U.Btn("Прослушать", async () => { if (await Tts.GetRuAsync("Привет! Это русский голос.") is { } f) await Speech.PlayFileAsync(f); }, "Ghost", "").Also(b => b.IsEnabled = Platform.SystemVoice.HasRu)));
        if (!Tts.HasVoice)
            sp.Children.Add(U.T("Сейчас японский звучит только из кэша. Установи VOICEVOX ниже — это лучший вариант.", "Body", 13, U.B("Gold")).M(0, 4, 0, 8));

        var voices = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        voices.Children.Add(U.T("Голос по умолчанию:", "Muted").CenterV().M(0, 0, 10, 0));
        foreach (var (male, name) in new[] { (false, "женский"), (true, "мужской") })
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = name, GroupName = "voice", IsChecked = p.MaleVoice == male };
            rb.Checked += (_, _) => { p.MaleVoice = male; ProgressStore.Notify(); Speech.Say("こんにちは、よろしくお願いします。"); };
            voices.Children.Add(rb);
        }
        sp.Children.Add(voices);

        // Озвучка курса заранее: в фоне, с паузой и ограничением размера кэша
        var info = U.T($"Кэш озвучки: {Storage.Format(Storage.Size(cacheItem))}", "Muted").M(0, 14, 0, 6);
        var bar = new ProgressBar { Maximum = 1, IsVisible = false, Margin = new Thickness(0, 4, 0, 4) };
        var limits = new long[] { 500L << 20, 1L << 30, 2L << 30 };
        int limit = 1;
        var limitPick = ChoiceS(new[] { "0", "1", "2" }, "1", v => limit = int.Parse(v), v => "до " + Storage.Format(limits[int.Parse(v)]));
        var btn = U.Btn(_prefetchCts != null ? "Пауза" : "Озвучить весь курс заранее", () => { }, "", "");
        btn.IsEnabled = Tts.UseOffline;
        btn.Click += async (_, _) =>
        {
            if (_prefetchCts != null) { _prefetchCts.Cancel(); return; }
            _prefetchCts = new CancellationTokenSource();
            btn.Content = "Пауза"; bar.IsVisible = true;
            var texts = Db.Words.Select(w => w.Kana)
                .Concat(Db.SpeechLessons.SelectMany(l => l.Items).Select(i => i.Jp))
                .Concat(Db.Grammar.SelectMany(g => g.Points).SelectMany(pt => pt.Examples).Select(e => e.Jp))
                .Concat(Db.Course.SelectMany(u => u.Examples).Select(e => e.Jp))
                .Concat(Db.Kanji.SelectMany(k => k.Examples).Select(e => e.Kana))
                .Concat(Db.Dialogs.SelectMany(d => d.Lines).Select(l => l.Jp))
                .Distinct().ToList();
            var prog = new Progress<(int done, int total)>(v => { bar.Value = (double)v.done / v.total; info.Text = $"Озвучено {v.done} из {v.total}…"; });
            int ok = await Tts.PrefetchAsync(texts, prog, _prefetchCts.Token, limits[limit]);
            bool paused = _prefetchCts.IsCancellationRequested;
            _prefetchCts = null;
            info.Text = paused ? $"Пауза: озвучено {ok}. Нажми ещё раз, чтобы продолжить."
                : Tts.CacheSizeBytes() > limits[limit] ? $"Достигнут предел кэша. Озвучено {ok} из {texts.Count}."
                : $"Готово! Курс озвучен ({Storage.Format(Storage.Size(cacheItem))}).";
            btn.Content = "Озвучить весь курс заранее"; bar.IsVisible = false;
        };
        var clear = U.Btn("Очистить кэш", () => { Storage.Clear(cacheItem); info.Text = "Кэш озвучки очищен."; }, "Ghost", "");
        sp.Children.Add(info);
        if (!Tts.UseOffline) sp.Children.Add(U.T("Озвучка курса заранее работает с VOICEVOX.", "Muted", 12.5));
        sp.Children.Add(limitPick);
        sp.Children.Add(bar);
        sp.Children.Add(U.Row(8, btn, clear).M(0, 6, 0, 0));
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
