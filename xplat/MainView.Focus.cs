using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Matsubakira.Services;

namespace Matsubakira;

/// <summary>Фокус-таймер в строке заголовка: меню выбора длительности, обратный отсчёт и окно «пора отдохнуть».</summary>
public partial class MainView
{
    Button? _focusBtn;
    TextBlock? _focusText;

    Button BuildFocusButton()
    {
        _focusText = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { Controls.Art.Icon("tomato", 20), _focusText } };
        _focusBtn = new Button { Content = content, Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Height = 32 };
        _focusBtn.Classes.Add("Ghost");
        _focusBtn.Click += (_, _) => FocusMenu().ShowAt(_focusBtn);
        FocusTimer.Changed += UpdateFocus;
        FocusTimer.Finished += FocusFinished;
        UpdateFocus();
        return _focusBtn;
    }

    MenuFlyout FocusMenu()
    {
        var m = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Add(string text, Action a) { var it = new MenuItem { Header = text }; it.Click += (_, _) => a(); m.Items.Add(it); }
        if (FocusTimer.Running)
        {
            Add(FocusTimer.OnBreak ? "Закончить перерыв" : "Остановить таймер", FocusTimer.Stop);
            Add("Начать заново (25 минут)", () => FocusTimer.Start(25));
        }
        else
        {
            Add("Фокус 25 минут", () => StartFocus(25));
            Add("Фокус 50 минут", () => StartFocus(50));
            Add("Короткий подход 15 минут", () => StartFocus(15));
        }
        return m;
    }

    public static void StartFocus(int minutes)
    {
        FocusTimer.Start(minutes);
        Shell.Toast($"Фокус {minutes} минут — уведомления потом. Таймер в правом верхнем углу", "集中");
    }

    void UpdateFocus()
    {
        if (_focusBtn == null || _focusText == null) return;
        _focusText.Text = FocusTimer.Running ? (FocusTimer.OnBreak ? "перерыв " : "") + FocusTimer.Label : "Фокус";
        _focusText.Foreground = FocusTimer.Running ? U.B(FocusTimer.OnBreak ? "Good" : "Accent") : U.B("Muted");
        ToolTip.SetTip(_focusBtn, FocusTimer.Running ? "Фокус-таймер идёт — нажми, чтобы остановить" : "Фокус-таймер: 25 минут занятий, потом перерыв (Ctrl+T)");
    }

    void FocusFinished(bool work)
    {
        Sfx.Done();
        if (Win is { } w && !w.IsActive) Tray.Notify(work ? "Фокус-сессия завершена" : "Перерыв окончен", work ? "Отличная работа! Отдохни 5 минут." : "Продолжим занятия?");
        var sp = U.Stack(
            Controls.Art.Mascot(110, work ? Controls.Mood.Joy : Controls.Mood.Happy).Center(),
            U.Jp(work ? "お疲れ様！" : "さあ、続けよう！", 28, U.B("Accent"), true).Center().M(0, 8, 0, 0),
            U.T(work ? $"{FocusTimer.Minutes} минут фокуса — +10 XP. Встань, разомнись, посмотри вдаль." : "Перерыв окончен. Ещё один подход?", "Body", 14.5).Center().Also(t => t.TextWrapping = TextWrapping.Wrap).M(0, 8, 0, 0),
            U.T($"Сессий фокуса всего: {ProgressStore.P.FocusSessions}", "Muted", 12).Center().M(0, 4, 0, 0),
            U.Row(10,
                work ? U.Btn("Перерыв 5 минут", () => { CloseOverlay(); FocusTimer.Start(5, true); }, "Primary") : U.Btn("Ещё 25 минут", () => { CloseOverlay(); FocusTimer.Start(25); }, "Primary"),
                U.Btn("Закрыть", CloseOverlay)).Center().M(0, 18, 0, 0));
        ShowOverlay(sp, 460);
    }
}
