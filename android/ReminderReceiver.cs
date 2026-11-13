using Android.App;
using Android.Content;
using Matsubakira.Services;

namespace Matsubakira.Droid;

/// <summary>
/// Ежедневное напоминание при закрытом приложении: системный будильник на 19:00. Ставится при каждом уходе
/// из приложения (OnPause) — если сегодня уже занимались, на завтра. Срабатывая, проверяет, не было ли занятий.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    const int Hour = 19;

    public static void Schedule()
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var am = (AlarmManager?)ctx.GetSystemService(Context.AlarmService);
            var pi = PendingIntent.GetBroadcast(ctx, 0, new Intent(ctx, typeof(ReminderReceiver)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            if (am == null || pi == null) return;
            if (!ProgressStore.P.Reminders) { am.Cancel(pi); return; }
            var at = DateTime.Today.AddHours(Hour);
            if (ProgressStore.TodayXp > 0 || DateTime.Now >= at) at = at.AddDays(1);
            long ms = new DateTimeOffset(at).ToUnixTimeMilliseconds();
            // неточный будильник: точный требует отдельного разрешения, а минута-другая здесь не важна
            if (OperatingSystem.IsAndroidVersionAtLeast(23)) am.SetAndAllowWhileIdle(AlarmType.RtcWakeup, ms, pi);
            else am.Set(AlarmType.RtcWakeup, ms, pi);
        }
        catch (Exception e) { CrashReport.Warn("Напоминание", e); }
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            if (!ProgressStore.IsLoaded) ProgressStore.Load();
            var p = ProgressStore.P;
            if (ProgressStore.TodayXp == 0)
            {
                int due = p.Cards.Values.Count(c => c.Due <= DateTime.Now);
                string text = p.Streak > 0 ? $"Серия {p.Streak} {MainView.Plural(p.Streak, "день", "дня", "дней")} — пять минут сегодня, и она продолжится."
                    : due > 0 ? $"{due} карточек ждут повторения — это займёт пару минут." : "Пять минут японского — и день засчитан.";
                Tray.Notify("Пора позаниматься", text);
            }
        }
        catch (Exception e) { CrashReport.Warn("Напоминание", e); }
        Schedule(); // следующее — на завтра
    }
}
