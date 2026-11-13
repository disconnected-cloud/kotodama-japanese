using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Threading;
using Matsubakira.Services;
using Matsubakira.Views;

namespace Matsubakira.Droid;

/// <summary>
/// Аудиоплеер «Слушай без экрана» при выключенном экране: без службы переднего плана Android замораживает
/// свёрнутое приложение через несколько секунд, а процессор засыпает между фразами. Служба держит уведомление
/// «Kotodama играет» (кнопка «Стоп») и частичную блокировку сна, пока плеер играет.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    const string Channel = "player";
    const string StopAction = "kotodama.player.stop";
    const int Id = 7001;
    static PowerManager.WakeLock? _wake;

    /// <summary>Вызывается из общего кода (Platform.Hooks.BackgroundAudio), когда плеер начинает или перестаёт играть.</summary>
    public static void Set(bool playing)
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var i = new Intent(ctx, typeof(PlaybackService));
            if (!playing) { ctx.StopService(i); return; }
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) ctx.StartForegroundService(i); else ctx.StartService(i);
        }
        catch (Exception e) { CrashReport.Warn("Фоновое воспроизведение", e); }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            Dispatcher.UIThread.Post(Player.Stop);
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        var n = Build();
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(Id, n, ForegroundService.TypeMediaPlayback);
        else StartForeground(Id, n);
        if (_wake == null && GetSystemService(PowerService) is PowerManager pm)
        {
            _wake = pm.NewWakeLock(WakeLockFlags.Partial, "kotodama:player");
            _wake?.SetReferenceCounted(false);
        }
        _wake?.Acquire(3 * 60 * 60 * 1000); // на случай, если плеер забудут: не дольше трёх часов
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        try { if (_wake?.IsHeld == true) _wake.Release(); } catch { }
        if (OperatingSystem.IsAndroidVersionAtLeast(24)) StopForeground(StopForegroundFlags.Remove); else StopForeground(true);
        base.OnDestroy();
    }

    Notification Build()
    {
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && nm?.GetNotificationChannel(Channel) == null)
            nm?.CreateNotificationChannel(new NotificationChannel(Channel, "Аудиоплеер", NotificationImportance.Low) { Description = "Пока играет «Слушай без экрана»" });
        var open = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var pi = open == null ? null : PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var stop = PendingIntent.GetService(this, 1, new Intent(this, typeof(PlaybackService)).SetAction(StopAction), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var b = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, Channel) : new Notification.Builder(this);
        b.SetContentTitle("Kotodama · аудиоплеер").SetContentText("Слушай без экрана: японский — пауза — перевод")
         .SetSmallIcon(Resource.Drawable.icon).SetOngoing(true)
         .AddAction(new Notification.Action.Builder(0, "Стоп", stop).Build());
        if (pi != null) b.SetContentIntent(pi);
        return b.Build();
    }
}
