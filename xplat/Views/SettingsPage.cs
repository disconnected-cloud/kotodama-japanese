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

public sealed class SettingsPage : UserControl, IPage
{
    public string Title => "Настройки";
    bool _confirmReset;

    public void OnShown()
    {
        var p = ProgressStore.P;
        var root = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(U.Header("設", "Настройки", "Темп обучения, оформление и звук"));

        root.Children.Add(Section("Дневная цель", "Сколько XP набирать в день. 50 XP ≈ 15 минут занятий.",
            Choice(new[] { 20, 50, 100, 150 }, p.DailyGoal, v => p.DailyGoal = v, v => $"{v} XP")));
        root.Children.Add(Section("Новых карточек за сессию", "Сколько новых слов вводить за один подход.",
            Choice(new[] { 5, 10, 15, 20 }, p.NewPerSession, v => p.NewPerSession = v, v => v.ToString())));

        root.Children.Add(Section("Отображение и звук", null, U.Stack(
            Toggle("Показывать ромадзи (латиницу) на карточках", p.ShowRomaji, v => p.ShowRomaji = v),
            Toggle("Автоматически озвучивать слова", p.AutoSpeak, v => p.AutoSpeak = v))));

        root.Children.Add(Section("Внешний вид", "Тема и масштаб применяются после перезапуска.", U.Stack(
            Choice(new[] { 0, 1 }, p.LightTheme ? 1 : 0, v => { p.LightTheme = v == 1; AskRestart(); }, v => v == 0 ? "Тёмная" : "Светлая"),
            Choice(new[] { 90, 100, 115, 130 }, (int)Math.Round(p.UiScale * 100), v => { p.UiScale = v / 100.0; AskRestart(); }, v => v + "%").M(0, 10, 0, 0),
            ChoiceS(new[] { "sakura", "mint", "indigo", "amber" }, p.AccentColor, v => { p.AccentColor = v; AskRestart(); }, v => v switch { "mint" => "Мята", "indigo" => "Индиго", "amber" => "Янтарь", _ => "Сакура" }).M(0, 10, 0, 0),
            Toggle("Анимированное интро при запуске", p.Intro, v => p.Intro = v).M(0, 12, 0, 0),
            Toggle("Напоминания Windows (повторение, серия)", p.Reminders, v => p.Reminders = v),
            Toggle("Звуки ответов", p.Sounds, v => { p.Sounds = v; if (v) Sfx.Ok(); }))));
        root.Children.Add(Section("Движок озвучки", "Авто: офлайн-голос VOICEVOX, если установлен, иначе нейро-голос онлайн.",
            ChoiceS(new[] { "auto", "online", "offline" }, p.TtsEngine, v => { p.TtsEngine = v; OnShown(); }, v => v switch { "auto" => "Авто", "online" => "Онлайн (Microsoft)", _ => "Офлайн (VOICEVOX)" })));
        root.Children.Add(Section("Офлайн-голоса VOICEVOX", "Бесплатный японский синтез, работает без интернета. ~100 голосов в 27 пакетах. Условия: при публикации озвучки указывай «VOICEVOX:имя персонажа».", VoicevoxBlock()));
        root.Children.Add(Section("Голос", "Нейросетевая озвучка Microsoft Neural. Каждая фраза сохраняется на диск — повторно звучит мгновенно и без интернета.", VoiceBlock()));
        root.Children.Add(SpeechUi.ModelCard(OnShown).M(0, 0, 0, 16));
        root.Children.Add(Section("Микрофон", $"Режим: {(p.MicMode == "auto" ? $"авто-стоп после паузы {p.MicPause:0.0} с" : "ручная остановка")}.",
            U.Btn("Проверка и настройка микрофона", () => Shell.Open(new MicCheckPage()), "", "\uE720")));
        if (Asr.Ready)
            root.Children.Add(Section("Модель распознавания", "Точная лучше слышит ошибки, быстрая — для слабых компьютеров. Переключение скачает выбранную модель.",
                Choice(new[] { 0, 1 }, Asr.ModelId == Asr.Models[0].id ? 0 : 1, v => { ProgressStore.P.AsrModel = Asr.Models[v].id; Asr.Unload(); OnShown(); }, v => Asr.Models[v].title)));

        var reset = U.Btn("Сбросить весь прогресс", () => { }, "", "");
        reset.Click += (_, _) =>
        {
            if (!_confirmReset) { _confirmReset = true; ((TextBlock)((StackPanel)reset.Content).Children[1]).Text = "Точно? Нажми ещё раз — это необратимо"; reset.BorderBrush = U.B("Bad"); return; }
            ProgressStore.Reset();
            _confirmReset = false;
            Shell.Toast("Прогресс сброшен. Начинаем с чистого листа!", "新");
            OnShown();
        };
        root.Children.Add(Section("Резервная копия", "Сохрани прогресс в файл — пригодится при переустановке Windows или переносе на другой компьютер.", U.Row(10,
            U.Btn("Экспорт прогресса", ExportProgress, "", "\uE74E"),
            U.Btn("Импорт из файла", ImportProgress, "", "\uE8E5"))));
        root.Children.Add(Section("Достижения", $"Открыто {p.Achievements.Count} из {Achievements.All.Length}", AchievementsGrid()));
        root.Children.Add(Section("Обновления", $"Установлена версия {Updater.Current.ToString(3)}. Новые версии берутся из github.com/{Updater.Repo}.", UpdateBlock()));
        var report = U.Row(10,
            U.Btn("Сообщить об ошибке", () => CrashReport.Open(), "", "\uEBE8"),
            U.Btn("Предложить идею", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://github.com/{Updater.Repo}/issues/new?labels=enhancement") { UseShellExecute = true }); } catch { } }, "Ghost", "\uEA80"));
        root.Children.Add(Section("Обратная связь", CrashReport.LastEntry() is { } last ? "В журнале есть ошибка — отчёт откроется на GitHub уже заполненным, ты только нажмёшь «Submit»." : "Нашёл ошибку или есть идея? Откроется форма на GitHub.", report));
        root.Children.Add(Section("Хранилище", $"Всего занято: {Storage.Format(Storage.Total)} · папка: {ProgressStore.Folder}", StorageBlock()));
        root.Children.Add(Section("Данные", $"Прогресс хранится локально: {ProgressStore.Folder}", reset));

        root.Children.Add(Section("Открытые данные", null, U.Stack(
            U.T("Словарь: JMdict © EDRDG, лицензия CC BY-SA 4.0 (сборка jmdict-simplified).", "Muted"),
            U.T("Порядок черт: KanjiVG © Ulrich Apel, лицензия CC BY-SA 3.0.", "Muted"),
            U.T("Питч-акцент: Kanjium (CC BY-SA 4.0). Части кандзи: KRADFILE, KANJIDIC2 © EDRDG (CC BY-SA 4.0).", "Muted"),
            U.T("Распознавание речи: OpenAI Whisper (MIT) через whisper.cpp / Whisper.net.", "Muted"),
            U.T("Иллюстрации: Microsoft Fluent Emoji (MIT).", "Muted"),
            U.T("Офлайн-синтез: VOICEVOX (голоса — по условиям персонажей). Примеры фраз: Tatoeba (CC BY 2.0 FR).", "Muted"),
            U.T("Морфологический разбор при подготовке латиницы: MeCab + UniDic.", "Muted"))));
        root.Children.Add(U.T("Kotodama 1.1 · 言霊 — «душа слова»: правильно произнесённое слово обретает силу.", "Muted").M(0, 8, 0, 0));
        Content = U.Page(root);
        Anim.Stagger(root, 50);
    }

    Control UpdateBlock()
    {
        var p = ProgressStore.P;
        var sp = new StackPanel();
        var status = U.T(Updater.Available != null ? $"Доступна версия {Updater.Available.Tag}" : "", "Body", 14, U.B("Good"));
        var bar = new ProgressBar { Maximum = 1, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        var install = U.Btn("Установить обновление", () => { }, "Primary", "\uE896");
        install.IsVisible = Updater.Available != null ? true : false;
        install.Click += async (_, _) =>
        {
            if (Updater.Available is not { } r) return;
            install.IsEnabled = false; bar.IsVisible = true;
            status.Text = $"Скачиваю {r.Tag}… после загрузки приложение перезапустится";
            try { await Updater.InstallAsync(r, new Progress<double>(v => bar.Value = v)); }
            catch (Exception ex) { status.Text = "Не удалось обновиться: " + ex.Message; install.IsEnabled = true; }
        };
        var check = U.Btn("Проверить обновления", async () => { }, "", "\uE72C");
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false; status.Text = "Проверяю…"; status.Foreground = U.B("Muted");
            var r = await Updater.CheckAsync();
            status.Text = r != null ? $"Доступна версия {r.Tag}!" : "У тебя последняя версия.";
            status.Foreground = U.B(r != null ? "Good" : "Muted");
            install.IsVisible = r != null ? true : false;
            check.IsEnabled = true;
        };
        sp.Children.Add(U.Row(10, check, install));
        sp.Children.Add(status.M(0, 10, 0, 0));
        sp.Children.Add(bar);
        sp.Children.Add(Toggle("Проверять обновления автоматически (раз в день)", p.AutoUpdate, v => p.AutoUpdate = v).M(0, 10, 0, 0));
        return sp;
    }

    Control StorageBlock()
    {
        var sp = new StackPanel();
        foreach (var it in Storage.Items)
        {
            long size = Storage.Size(it);
            var row = U.Cols("*", "auto", "auto");
            row.Margin = new Thickness(0, 0, 0, 10);
            row.Children.Add(U.Stack(U.T(it.Title, "H3", 14), U.T(it.Desc, "Muted", 12)));
            row.Children.Add(U.T(Storage.Format(size), "Body", 13.5, U.B("Muted")).CenterV().M(12, 0, 12, 0).Col(1));
            var item = it;
            var b = U.Btn("Очистить", async () =>
            {
                if (!await Shell.Confirm($"Удалить «{item.Title}» ({Storage.Format(size)})?")) return;
                bool ok = Storage.Clear(item);
                Shell.Toast(ok ? "Удалено" : "Часть файлов занята — удалятся при следующем запуске", "削除");
                OnShown();
            }, "", "\uE74D");
            b.IsEnabled = size > 0;
            row.Children.Add(b.CenterV().Col(2));
            sp.Children.Add(row);
        }
        var all = U.Btn("Удалить всё скачанное и кэш", async () =>
        {
            if (!await Shell.Confirm("Удалить кэш озвучки, модели Whisper, VOICEVOX и журналы? Прогресс обучения сохранится.")) return;
            bool ok = Storage.Items.Select(Storage.Clear).ToList().All(x => x);
            Shell.Toast(ok ? "Всё скачанное удалено" : "Часть файлов удалится при следующем запуске", "削除");
            OnShown();
        }, "", "\uE74D");
        all.HorizontalAlignment = HorizontalAlignment.Left; all.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(all);
        var full = U.Btn("Полный сброс: удалить вообще всё, включая прогресс", async () =>
        {
            if (!await Shell.Confirm("Будут удалены ВСЕ данные приложения: прогресс, настройки, скачанные модели и голоса. Продолжить?")) return;
            foreach (var it in Storage.Items) Storage.Clear(it);
            ProgressStore.Reset();
            ProgressStore.P.Flags.Add("mig-v3");
            foreach (var it in Storage.Items) if (System.IO.Directory.Exists(it.Path) && it.IsDir) ProgressStore.P.Flags.Add("pending-delete:" + it.Key);
            ProgressStore.Save();
            Shell.Toast("Всё удалено — начинаем с чистого листа", "新");
            Shell.Go("home");
        }, "Ghost", "\uE7BA");
        full.HorizontalAlignment = HorizontalAlignment.Left; full.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(full);
        return sp;
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

    static StackPanel ChoiceS(string[] values, string current, Action<string> set, Func<string, string> label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var v in values)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = label(v), GroupName = group, IsChecked = v == current };
            rb.Checked += (_, _) => { set(v); ProgressStore.Notify(); };
            sp.Children.Add(rb);
        }
        return sp;
    }

    Control VoicevoxBlock()
    {
        var p = ProgressStore.P;
        var sp = new StackPanel();
        var installed = Voicevox.InstalledPacks;
        if (Voicevox.Downloaded && !Voicevox.Installed)
        {
            var chk = U.Btn("Проверить VOICEVOX ещё раз", async () => { }, "", "\uE72C");
            chk.HorizontalAlignment = HorizontalAlignment.Left;
            chk.Click += async (_, _) => { var (ok, msg) = await Voicevox.SelfCheckAsync(); Shell.Toast(msg, ok ? "声" : "!"); OnShown(); };
            sp.Children.Add(U.T("Файлы скачаны, но проверка синтеза не пройдена.", "Body", 13, U.B("Gold")));
            sp.Children.Add(chk.M(0, 8, 0, 8));
        }
        if (Voicevox.Installed)
        {
            var styles = Voicevox.InstalledStyles();
            sp.Children.Add(U.T($"Установлено пакетов: {installed.Count} · голосов: {styles.Count}", "Body", 13.5, U.B("Good")));
            sp.Children.Add(U.T("ГОЛОС ПО УМОЛЧАНИЮ", "Overline").M(0, 12, 0, 6));
            var wrap = new WrapPanel();
            uint cur = Voicevox.PickStyle(false);
            foreach (var st in styles.OrderBy(x => !x.Character.StartsWith("女声") && !x.Character.StartsWith("男声")).ThenBy(x => x.Title))
            {
                var rb = new RadioButton { Classes = { "ChipRadio" }, Content = st.Title,  GroupName = "vvstyle", IsChecked = st.Id == cur, Margin = new Thickness(0, 0, 6, 6) }.Tip(st.Original);
                var id = st.Id;
                rb.Checked += (_, _) => { p.OfflineStyleId = (int)id; ProgressStore.Notify(); if (Tts.UseOffline) Speech.Say("こんにちは、一緒に日本語を勉強しましょう。"); };
                wrap.Children.Add(rb);
            }
            sp.Children.Add(wrap);
        }
        sp.Children.Add(U.T(Voicevox.Installed ? "ДОБАВИТЬ ПАКЕТЫ" : "ВЫБЕРИ ПАКЕТЫ ГОЛОСОВ", "Overline").M(0, 14, 0, 6));
        var chosen = new HashSet<string>(Voicevox.Installed ? Array.Empty<string>() : new[] { "n0.vvm", "0.vvm" });
        var list = new WrapPanel();
        foreach (var pack in Voicevox.Catalog.Where(pk => !installed.Contains(pk.File)))
        {
            var names = string.Join(", ", pack.Chars.Select(c => VoiceNames.Name(c.Name)));
            var tb = new ToggleButton
            {
                Classes = { "Chip" }, IsChecked = chosen.Contains(pack.File),
                Content = (pack.File == "n0.vvm" ? "★ " : "") + (names.Length > 34 ? names[..34] + "…" : names) + $"  ·  {pack.Size / 1_000_000} МБ",
                
            }.Tip((pack.File == "n0.vvm" ? "Естественные взрослые голоса — лучший выбор для учёбы\n" : "") + string.Join(", ", pack.Chars.Select(c => c.Name)));
            var f = pack.File;
            tb.Checked += (_, _) => chosen.Add(f);
            tb.Unchecked += (_, _) => chosen.Remove(f);
            list.Children.Add(tb);
        }
        sp.Children.Add(list);
        var bar = new ProgressBar { Maximum = 1, IsVisible = false, Margin = new Thickness(0, 8, 0, 4) };
        var status = U.T(Voicevox.LastError ?? "", "Muted");
        var btn = U.Btn(Voicevox.BaseInstalled ? "Скачать выбранные пакеты" : "Установить VOICEVOX (+ ~30 МБ компонентов)", () => { }, "Primary", "\uE896");
        btn.HorizontalAlignment = HorizontalAlignment.Left; btn.Margin = new Thickness(0, 8, 0, 0);
        btn.Click += async (_, _) =>
        {
            if (chosen.Count == 0 && Voicevox.BaseInstalled) { Shell.Toast("Выбери хотя бы один пакет", "!"); return; }
            btn.IsEnabled = false; bar.IsVisible = true;
            var prog = new Progress<(double v, string what)>(x => { bar.Value = x.v; status.Text = $"Загрузка: {x.what}… {x.v * 100:0}%"; });
            try
            {
                await Voicevox.InstallAsync(chosen, prog, CancellationToken.None);
                status.Text = "Проверяю синтез в безопасном режиме…";
                var (ok, msg) = await Voicevox.SelfCheckAsync();
                if (!ok) { status.Text = msg + ". Онлайн-голос продолжит работать."; btn.IsEnabled = true; return; }
                if (p.TtsEngine == "online") p.TtsEngine = "auto";
                ProgressStore.Notify();
                Shell.Toast("Офлайн-голоса установлены!", "声");
                OnShown();
                Speech.Say("こんにちは、一緒に日本語を勉強しましょう。");
            }
            catch (Exception ex) { status.Text = "Ошибка: " + ex.Message; btn.IsEnabled = true; }
        };
        sp.Children.Add(btn);
        sp.Children.Add(bar);
        sp.Children.Add(status);
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

    static Control AchievementsGrid()
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
        var voices = new StackPanel { Orientation = Orientation.Horizontal };
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

    static StackPanel Choice(int[] values, int current, Action<int> set, Func<int, string> label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var v in values)
        {
            var rb = new RadioButton { Classes = { "ChipRadio" }, Content = label(v), GroupName = group, IsChecked = v == current };
            rb.Checked += (_, _) => { set(v); ProgressStore.Notify(); };
            sp.Children.Add(rb);
        }
        return sp;
    }

    static ToggleButton Toggle(string text, bool on, Action<bool> set)
    {
        var tb = new ToggleButton { Classes = { "Chip" }, Content = text, IsChecked = on, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(16, 10, 16, 10) };
        tb.Checked += (_, _) => { set(true); ProgressStore.Notify(); };
        tb.Unchecked += (_, _) => { set(false); ProgressStore.Notify(); };
        return tb;
    }
}
