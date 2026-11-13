using Avalonia.Threading;

namespace Matsubakira.Services;

/// <summary>
/// Фокус-таймер («помидор»): 25 минут занятий → 5 минут отдыха. Секундный таймер работает только пока идёт отсчёт,
/// в простое приложение ничего не тикает.
/// </summary>
public static class FocusTimer
{
    public static bool Running => _timer?.IsEnabled == true;
    public static bool OnBreak { get; private set; }
    public static TimeSpan Remaining => Running ? _end - DateTime.Now : TimeSpan.Zero;
    public static int Minutes { get; private set; } = 25;
    public static event Action? Changed;
    /// <summary>Отсчёт закончился: true — закончилось занятие (пора отдыхать), false — закончился перерыв.</summary>
    public static event Action<bool>? Finished;

    static DispatcherTimer? _timer;
    static DateTime _end;

    public static void Start(int minutes, bool breakTime = false)
    {
        Minutes = minutes;
        OnBreak = breakTime;
        _end = DateTime.Now.AddMinutes(minutes);
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
        Changed?.Invoke();
    }

    public static void Stop()
    {
        _timer?.Stop();
        OnBreak = false;
        Changed?.Invoke();
    }

    static void Tick()
    {
        if (DateTime.Now < _end) { Changed?.Invoke(); return; }
        bool work = !OnBreak;
        _timer?.Stop();
        OnBreak = false;
        if (work)
        {
            ProgressStore.P.FocusSessions++;
            ProgressStore.AddXp(10);
        }
        Changed?.Invoke();
        Finished?.Invoke(work);
    }

    public static string Label => Running ? $"{(int)Remaining.TotalMinutes:00}:{Remaining.Seconds:00}" : "";
}
