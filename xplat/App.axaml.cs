using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Fonts;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Matsubakira.Services;

namespace Matsubakira;

public partial class App : Application
{
    public override void Initialize()
    {
        // Интерфейс русский — даты и месяцы тоже по-русски, независимо от языка системы
        var ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.CurrentCulture = ru;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.CurrentUICulture = ru;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Perf.Mark("Avalonia готова");
        if (!ProgressStore.IsLoaded) ProgressStore.Load(); // на телефоне нет Program.Main
        bool light = ProgressStore.P.LightTheme || Environment.GetCommandLineArgs().Contains("--light");
        RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        bool night = !light && (ProgressStore.P.NightTheme || Environment.GetCommandLineArgs().Contains("--night"));
        ApplyPalette(Resources, light, ProgressStore.P.AccentColor, night);
        ApplyJpFont(Resources, ProgressStore.P.JpFontName);
        Anim.ApplyMotion(ProgressStore.P.Motion);
        // Телефон: любая надпись переносится на следующую строку, а не обрезается краем узкого экрана.
        // Где перенос не нужен (иконки, цифры), он ничего не меняет; явный TextWrapping в коде сильнее стиля.
        if (Shell.Compact)
            Styles.Add(new Style(x => x.OfType<TextBlock>()) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } });

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashReport.Log(e.Exception);
            e.Handled = true;
            // Прогресс отсюда не сохраняем: сбой мог прервать изменение на полпути. Серия ошибок подряд — предлагаем отчёт.
            var now = DateTime.Now;
            _errors.Enqueue(now);
            while (_errors.Count > 0 && now - _errors.Peek() > TimeSpan.FromMinutes(1)) _errors.Dequeue();
            try
            {
                if (Shell.Main == null) return;
                if (_errors.Count >= 3 && !_reportAsked)
                {
                    _reportAsked = true;
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (await Shell.Confirm("Несколько ошибок подряд. Отправить отчёт разработчику? Откроется страница с текстом ошибки — ты увидишь всё перед отправкой.", "Отправить отчёт"))
                            CrashReport.Open();
                    });
                }
                else Shell.Toast("Что-то пошло не так — записал в журнал, продолжаем", "!"); // некритичное — коротким уведомлением
            }
            catch { } // уведомление об ошибке не показалось — ошибка уже в журнале
        };

        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView(); // телефон: интерфейс без окна
#if !MOBILE
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) => ProgressStore.Save();
            desktop.Exit += (_, _) => ProgressStore.Save();
        }
#endif
        // Тяжёлое — в фоне, чтобы окно появилось сразу
        // данные курса нужны главной уже для первого кадра; словарь и акценты — после него (Perf.WarmUpAfterFirstFrame)
        Task.Run(() => { Db.Preload(); Perf.Mark("данные курса загружены"); });
        if (Environment.GetCommandLineArgs().Skip(1).All(a => a == SingleInstance.RestartArg) && Voicevox.Downloaded && !ProgressStore.P.Flags.Contains("vv-ok") && !ProgressStore.P.Flags.Contains("vv-crashed"))
            _ = Task.Run(async () =>
            {
                var (ok, _) = await Voicevox.SelfCheckAsync();
                if (ok) Dispatcher.UIThread.Post(() => Shell.Toast("Офлайн-голоса VOICEVOX готовы", "声"));
            });
        base.OnFrameworkInitializationCompleted();
    }

    static readonly Queue<DateTime> _errors = new();
    static bool _reportAsked;

    /// <summary>
    /// Запасной шрифт для всего интерфейса: значки ✓ ★ ♥ ⚠ и эмодзи 🔊 💡, которых нет в системном шрифте
    /// (на Android в Roboto их нет — без этого они просто не видны). Глифы — в Kotodama Icons (tools/make_icon_font.py).
    /// </summary>
    public static AppBuilder WithFonts(AppBuilder b) => b.With(new FontManagerOptions
    {
        FontFallbacks = OperatingSystem.IsAndroid()
            // Android: японский шрифт свой, в APK (Noto Sans JP, обычный и жирный). На части телефонов (китайские прошивки)
            // системный CJK-шрифт без каны — хирагана и катакана были квадратиками, а иероглифы видны
            ? new[] { new FontFallback { FontFamily = new FontFamily("avares://Kotodama/Assets/fonts#Kotodama Icons") }, new FontFallback { FontFamily = new FontFamily(AndroidJp) } }
            : new[] { new FontFallback { FontFamily = new FontFamily("avares://Kotodama/Assets/fonts#Kotodama Icons") } },
    });

    /// <summary>Палитра «тихий минимализм, один акцент (сакура)». Кисти кладутся в ресурсы до создания окна.</summary>
    /// <summary>Шрифты для японского: учебный (как в школьных прописях) удобнее для запоминания формы знаков.</summary>
    public static readonly (string name, string title, string family)[] JpFonts =
    {
        ("", "Noto Sans JP (встроенный)", ""),
        ("kyokasho", "Учебник", "UD Digi Kyokasho N-R"),
        ("ud", "Чёткий", "BIZ UDGothic"),
        ("meiryo", "Meiryo", "Meiryo"),
        ("mincho", "Книжный", "Yu Mincho"),
    };

    const string AndroidJp = "avares://Kotodama/Assets/fonts#Noto Sans JP";

    /// <summary>Встроенные шрифты (SIL OFL): японский текст выглядит одинаково на любом компьютере, Yu Gothic на тёмном фоне был тонким.</summary>
    const string EmbeddedSans = "avares://Kotodama/Assets/fonts#Noto Sans JP", EmbeddedSerif = "avares://Kotodama/Assets/fonts#Noto Serif JP";

    const string JpFallback = "Yu Gothic UI, Meiryo, Noto Sans CJK JP, Noto Sans JP, Source Han Sans JP, IPAGothic, TakaoGothic, VL Gothic, sans-serif";

    public static bool FontInstalled(string family) =>
        family.Length == 0 || FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));

    static void ApplyJpFont(IResourceDictionary r, string key)
    {
        if (OperatingSystem.IsAndroid())
        {
            r["JpFont"] = r["JpSerif"] = new FontFamily(AndroidJp);
            return;
        }
        r["JpSerif"] = new FontFamily(EmbeddedSerif + ", " + JpFallback);
        var f = JpFonts.FirstOrDefault(x => x.name == key).family;
        r["JpFont"] = string.IsNullOrEmpty(f) || !FontInstalled(f) ? new FontFamily(EmbeddedSans + ", " + JpFallback) : new FontFamily(f + ", " + JpFallback);
    }

    /// <summary>Цвета текста проверены на контраст WCAG: основной ≥ 10:1, второстепенный и цветной ≥ 4,5:1 на всех фонах.</summary>
    public static void ApplyPalette(IResourceDictionary r, bool light, string accent, bool night = false)
    {
        var dark = new Dictionary<string, string>
        {
            ["Bg"] = "#0F1014", ["Sidebar"] = "#0F1014", ["Surface"] = "#16171C", ["SurfaceSolid"] = "#16171C", ["Surface2"] = "#1D1F25",
            ["Surface3"] = "#272A31", ["Stroke"] = "#22242B", ["StrokeStrong"] = "#33363F", ["Text"] = "#EDEDF0", ["Muted"] = "#A0A3AD",
            ["Faint"] = "#8A8E98", ["Accent"] = "#F07AA0", ["AccentHover"] = "#F48DAE", ["Accent2"] = "#9D8CFF", ["AccentSoft"] = "#1FF07AA0",
            ["Gold"] = "#E8B860", ["Good"] = "#5CC99A", ["GoodSoft"] = "#1F5CC99A", ["Bad"] = "#EF6F6F", ["BadSoft"] = "#1FEF6F6F",
            ["Info"] = "#6FA8F0", ["AccentGradient"] = "#EE7AA0", ["GoldGradient"] = "#E2B15E", ["CoolGradient"] = "#6F94E8",
            ["GreenGradient"] = "#55BE90", ["GlassStroke"] = "#22242B", ["CardFill"] = "#16171C", ["HoverTint"] = "#FFFFFF",
            ["SelectedTint"] = "#0FFFFFFF", ["Thumb"] = "#1FFFFFFF", ["ThumbHover"] = "#40FFFFFF", ["CaptionHover"] = "#12FFFFFF", ["OnAccent"] = "#1A0B11",
        };
        var lite = new Dictionary<string, string>
        {
            ["Bg"] = "#F6F5F3", ["Sidebar"] = "#F6F5F3", ["Surface"] = "#FFFFFF", ["SurfaceSolid"] = "#FFFFFF", ["Surface2"] = "#F0EEEB",
            ["Surface3"] = "#E4E1DD", ["Stroke"] = "#E6E3DF", ["StrokeStrong"] = "#D3CFCA", ["Text"] = "#1C1B1F", ["Muted"] = "#5C5966",
            ["Faint"] = "#6E6A76", ["Accent"] = "#B8335F", ["AccentHover"] = "#C8406E", ["Accent2"] = "#6F5BD9", ["AccentSoft"] = "#1FD9487A",
            ["Gold"] = "#86600F", ["Good"] = "#1F7A52", ["GoodSoft"] = "#1F2E9E6E", ["Bad"] = "#BC3434", ["BadSoft"] = "#1FD64545",
            ["Info"] = "#2A63B3", ["AccentGradient"] = "#DD5584", ["GoldGradient"] = "#D19A3A", ["CoolGradient"] = "#4F7FD9",
            ["GreenGradient"] = "#3AA77A", ["GlassStroke"] = "#E6E3DF", ["CardFill"] = "#FFFFFF", ["HoverTint"] = "#000000",
            ["SelectedTint"] = "#0D000000", ["Thumb"] = "#26000000", ["ThumbHover"] = "#4D000000", ["CaptionHover"] = "#12000000", ["OnAccent"] = "#FFFFFF",
        };
        // «Ночь»: настоящий чёрный (OLED, темнота), текст и акценты приглушены, чтобы не слепили
        var nightPal = new Dictionary<string, string>(dark)
        {
            ["Bg"] = "#000000", ["Sidebar"] = "#000000", ["Surface"] = "#0B0B0D", ["SurfaceSolid"] = "#0B0B0D", ["Surface2"] = "#121215",
            ["Surface3"] = "#1A1A1F", ["Stroke"] = "#16161A", ["StrokeStrong"] = "#24242A", ["Text"] = "#C4C2C8", ["Muted"] = "#9C9AA3",
            ["Faint"] = "#85838C", ["Accent"] = "#C2678A", ["AccentHover"] = "#CC7896", ["Accent2"] = "#8273CC", ["AccentSoft"] = "#1FC2678A",
            ["Gold"] = "#B8924D", ["Good"] = "#4A9F7B", ["GoodSoft"] = "#1F4A9F7B", ["Bad"] = "#BF5A5A", ["BadSoft"] = "#1FBF5A5A",
            ["Info"] = "#5A86BF", ["AccentGradient"] = "#BF6587", ["GoldGradient"] = "#B38C49", ["CoolGradient"] = "#5875B8",
            ["GreenGradient"] = "#46977A", ["GlassStroke"] = "#16161A", ["CardFill"] = "#0B0B0D", ["SelectedTint"] = "#0AFFFFFF",
        };
        var pal = light ? lite : night ? nightPal : dark;
        var (d, l) = accent switch
        {
            "mint" => ("#4FD1B5", "#167A66"),
            "indigo" => ("#8C9EFF", "#4553C9"),
            "amber" => ("#F2B45A", "#8A5C0E"),
            _ => ("", ""),
        };
        if (d.Length > 0)
        {
            var c = Color.Parse(light ? l : d);
            if (night) c = Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8));
            pal["Accent"] = pal["AccentGradient"] = pal["AccentHover"] = c.ToString();
            pal["AccentSoft"] = Color.FromArgb(0x1F, c.R, c.G, c.B).ToString();
        }
        if (ProgressStore.P.HighContrast || Environment.GetCommandLineArgs().Contains("--contrast")) // --contrast — снимки экранов в CI
        {
            // контраст текста ≥ 7:1 (WCAG AAA): подписи и второстепенный текст почти как основной, границы заметнее
            if (light) { pal["Text"] = "#000000"; pal["Muted"] = "#3A3842"; pal["Faint"] = "#5A5763"; pal["Stroke"] = "#9C98A3"; pal["StrokeStrong"] = "#6B6875"; }
            else { pal["Text"] = "#FFFFFF"; pal["Muted"] = "#D2D4DA"; pal["Faint"] = "#A9ACB5"; pal["Stroke"] = "#5B5F69"; pal["StrokeStrong"] = "#8E919B"; }
        }
        foreach (var (k, v) in pal) r[k] = new SolidColorBrush(Color.Parse(v));
    }
}
