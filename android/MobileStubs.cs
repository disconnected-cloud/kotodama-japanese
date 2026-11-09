using Android.App;
using Android.Content;
using Avalonia.Controls;

namespace Matsubakira.Services;

/// <summary>Телефон: вместо трея — системные уведомления Android (слово дня, напоминания).</summary>
public static class Tray
{
    const string Channel = "kotodama";
    static int _id = 1;
    static DateTime _lastReminder = DateTime.MinValue;

    public static void Init(Window w) { }
    public static void SetIcon(byte[] png, int due) { }
    public static void ShowWindow() { }
    public static void Remove() { }

    public static void Notify(string title, string text, string? clickTarget = null, bool force = false)
    {
        if (!force && !ProgressStore.P.Reminders) return;
        try
        {
            var ctx = Android.App.Application.Context;
            var nm = (NotificationManager?)ctx.GetSystemService(Context.NotificationService);
            if (nm == null) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && nm.GetNotificationChannel(Channel) == null)
                nm.CreateNotificationChannel(new NotificationChannel(Channel, "Слово дня и напоминания", NotificationImportance.Default));
            var open = ctx.PackageManager?.GetLaunchIntentForPackage(ctx.PackageName!);
            var pi = open == null ? null : PendingIntent.GetActivity(ctx, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var b = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(ctx, Channel) : new Notification.Builder(ctx);
            b.SetContentTitle(title).SetContentText(text).SetStyle(new Notification.BigTextStyle().BigText(text))
             .SetSmallIcon(Resource.Drawable.icon).SetAutoCancel(true);
            if (pi != null) b.SetContentIntent(pi);
            nm.Notify(_id++, b.Build());
        }
        catch { }
    }

    /// <summary>Раз в минуту, пока приложение открыто: слово дня по расписанию и напоминание о повторении.</summary>
    public static void Tick()
    {
        WordOfDay.Tick();
        if ((DateTime.Now - _lastReminder).TotalHours < 2) return;
        int due = ProgressStore.P.Cards.Values.Count(c => c.Due <= DateTime.Now);
        if (due >= 10) { Notify("Пора повторить", $"{due} карточек ждут повторения — это займёт пару минут."); _lastReminder = DateTime.Now; }
    }
}

/// <summary>На телефоне значок со счётчиком не нужен — число карточек видно на вкладке «Повтор».</summary>
public static class Badge
{
    public static void Update(Window w) { }
    public static bool LightTaskbar() => false;
}
