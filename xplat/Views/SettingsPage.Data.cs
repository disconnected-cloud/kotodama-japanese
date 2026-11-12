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

// Настройки: обновления и хранилище
public sealed partial class SettingsPage : UserControl, IPage
{
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
            if (it.Key == "llm" && size == 0) continue;
            var row = U.Cols("*", "auto", "auto");
            row.Margin = new Thickness(0, 0, 0, 10);
            row.Children.Add(U.Stack(U.T(it.Title, "H3", 14).Also(t => t.TextWrapping = TextWrapping.Wrap), U.T(it.Desc, "Muted", 12).Also(t => t.TextWrapping = TextWrapping.Wrap)));
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

    Control AutoBackupBlock()
    {
        var list = ProgressStore.Backups();
        var sp = new StackPanel();
        if (list.Count == 0) { sp.Children.Add(U.T("Первая копия появится при следующем запуске.", "Muted", 13)); return sp; }
        foreach (var f in list.Take(5))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(f).Replace("progress-", "");
            var label = DateTime.TryParse(name, out var d) ? d.ToString("d MMMM, dddd") : name;
            var row = U.Cols("*", "auto");
            row.Children.Add(U.T(label, "Body", 14).CenterV());
            var path = f;
            row.Children.Add(U.Btn("Вернуть", async () =>
            {
                if (!await Shell.Confirm($"Вернуть прогресс к копии от {label}? Всё, что сделано после неё, пропадёт (текущий файл сохранится рядом как progress.json.before-restore).", "Вернуть")) return;
                Shell.Toast(ProgressStore.RestoreBackup(path) ? "Прогресс восстановлен из копии" : "Копия повреждена — выбери другую", "復");
                OnShown();
            }, "Ghost", "").Col(1));
            sp.Children.Add(row.M(0, 0, 0, 4));
        }
        if (list.Count > 5) sp.Children.Add(U.T($"и ещё {list.Count - 5} в папке backups", "Muted", 12));
        return sp;
    }
}
