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

// Настройки: офлайн-голоса VOICEVOX
public sealed partial class SettingsPage : UserControl, IPage
{
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
            int clear = styles.Count(x => Voicevox.Clear.Contains(x.Id));
            sp.Children.Add(U.T($"Установлено пакетов: {installed.Count} · голосов: {styles.Count}, из них чётких: {clear}", "Body", 13.5, U.B("Good")));
            sp.Children.Add(U.T("Озвучка: " + Voicevox.Credits(), "Muted", 12).Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 4, 0, 0));
            uint cur = Voicevox.PickStyle(false);
            Control Group(string caption, IEnumerable<VoiceStyle> list)
            {
                var box = new StackPanel();
                box.Children.Add(U.T(caption, "Overline").M(0, 12, 0, 6));
                var wrap = new WrapPanel();
                foreach (var st in list)
                {
                    var rb = new RadioButton { Classes = { "ChipRadio" }, Content = st.Title, GroupName = "vvstyle", IsChecked = st.Id == cur, Margin = new Thickness(0, 0, 6, 6) }.Tip(st.Original);
                    var id = st.Id;
                    rb.Checked += (_, _) => { p.OfflineStyleId = (int)id; ProgressStore.Notify(); if (Tts.UseOffline) Speech.Say("こんにちは、一緒に日本語を勉強しましょう。"); };
                    rb.Tip($"{st.Original}\nVOICEVOX:{st.Character}");
                    wrap.Children.Add(rb);
                }
                box.Children.Add(wrap);
                return box;
            }
            var good = styles.Where(x => Voicevox.Clear.Contains(x.Id)).ToList();
            var female = good.Where(x => !Voicevox.IsMale(x)).ToList();
            var male = good.Where(Voicevox.IsMale).ToList();
            if (female.Count > 0) sp.Children.Add(Group("ЧЁТКИЕ ЖЕНСКИЕ ГОЛОСА — РЕКОМЕНДУЕМ", female));
            if (male.Count > 0) sp.Children.Add(Group("ЧЁТКИЕ МУЖСКИЕ (ДЛЯ МУЖСКИХ РОЛЕЙ В ДИАЛОГАХ)", male));
            var rest = styles.Where(x => !Voicevox.Clear.Contains(x.Id)).OrderBy(x => x.Title).ToList();
            if (rest.Count > 0)
            {
                var other = Group("ЭМОЦИОНАЛЬНЫЕ И ОСОБЫЕ СТИЛИ (шёпот, эмоции — не для учёбы)", rest);
                other.IsVisible = good.Count == 0 || rest.Any(x => x.Id == cur);
                var showAll = U.Btn($"Показать остальные стили ({rest.Count})", () => { }, "Ghost", "");
                showAll.HorizontalAlignment = HorizontalAlignment.Left;
                showAll.IsVisible = !other.IsVisible;
                showAll.Click += (_, _) => { other.IsVisible = true; showAll.IsVisible = false; };
                sp.Children.Add(showAll);
                sp.Children.Add(other);
            }
        }
        var missing = Voicevox.Recommended.Where(f => !installed.Contains(f)).ToArray();
        var chosen = new HashSet<string>(Voicevox.Installed ? Array.Empty<string>() : missing);
        if (missing.Length > 0)
        {
            long mb = Voicevox.Catalog.Where(pk => missing.Contains(pk.File)).Sum(pk => pk.Size) / 1_000_000;
            sp.Children.Add(new Border
            {
                Background = U.B("AccentSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), Margin = new Thickness(0, 14, 0, 0),
                // сетка, а не ряд: текст справа от значка переносится по ширине карточки
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("auto,*"), Children = { Art.Icon("sparkle", 36).CenterV(), U.Stack(
                    U.T("Рекомендуемый набор: чёткие голоса для учёбы", "H3", 14).Also(t => t.TextWrapping = TextWrapping.Wrap),
                    U.T($"Tsumugi, Himari, Metan, No.7 (диктор), Takehiro и Ryusei (мужские) — ровная дикция без придыхания. {(Voicevox.Installed ? $"Не хватает ~{mb} МБ" : $"~{mb} МБ")}.", "Muted", 12.5).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 520; })).CenterV().M(12, 0, 0, 0).Col(1) } },
            });
        }
        sp.Children.Add(U.T(Voicevox.Installed ? "ДОБАВИТЬ ПАКЕТЫ" : "ВЫБЕРИ ПАКЕТЫ ГОЛОСОВ", "Overline").M(0, 14, 0, 6));
        var list = new WrapPanel();
        int Cut = Shell.Compact ? 15 : 34; // на телефоне имена короче — полный список во всплывающей подсказке
        foreach (var pack in Voicevox.Catalog.Where(pk => !installed.Contains(pk.File)).OrderBy(pk => !Voicevox.Recommended.Contains(pk.File)))
        {
            bool rec = Voicevox.Recommended.Contains(pack.File);
            var names = string.Join(", ", pack.Chars.Select(c => VoiceNames.Name(c.Name)));
            var tb = new ToggleButton
            {
                Classes = { "Chip" }, IsChecked = chosen.Contains(pack.File), Margin = new Thickness(0, 0, 6, 6),
                Content = (rec ? "★ " : "") + (names.Length > Cut ? names[..Cut] + "…" : names) + $"  ·  {pack.Size / 1_000_000} МБ",
            }.Tip((rec ? "Рекомендуем: чёткие голоса\n" : pack.File == "n0.vvm" ? "Голоса с придыханием — могут звучать нечётко\n" : "") + string.Join(", ", pack.Chars.Select(c => c.Name)));
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
                if (!ok) { status.Text = msg + ". Пока будет звучать системный голос."; btn.IsEnabled = true; return; }
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
}
