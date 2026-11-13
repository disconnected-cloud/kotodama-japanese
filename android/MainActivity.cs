using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Matsubakira.Services;

namespace Matsubakira.Droid;

[Activity(Label = "Kotodama", Theme = "@style/KotodamaTheme", Icon = "@drawable/icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustResize, ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Звук, микрофон и системный голос — реализации Android
        Platform.Hooks.Playback = () => new AndroidPlayback();
        Platform.Hooks.Mic = () => new AndroidMic();
        Platform.Hooks.SystemTts = AndroidTts.SynthesizeAsync;
        Platform.Hooks.BackgroundAudio = PlaybackService.Set;
        ProgressStore.Load();
        return App.WithFonts(base.CustomizeAppBuilder(builder));
    }

    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
#if DEBUG
        // проверка в эмуляторе: adb shell am start … --es args "--page kana" (отладочные флаги, как на компьютере)
        if (Intent?.GetStringExtra("args") is { Length: > 0 } a)
        {
            Platform.Hooks.Args = new[] { "Kotodama" }.Concat(a.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            Platform.AudioPlayer.Muted = true;
            ProgressStore.ReadOnly = true;
        }
#endif
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();
        // Разрешения — не поверх знакомства, а когда они понадобились: микрофон — при первой записи (AndroidMic),
        // уведомления (слово дня, напоминания) — один раз после знакомства с приложением
        var f = ProgressStore.P.Flags;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && f.Contains("onboarded") && f.Add("asked-notif")
            && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 2);
    }

    /// <summary>Есть ли доступ к микрофону; если нет — показывает системный запрос.</summary>
    public static bool EnsureMic()
    {
        var a = Current;
        if (a == null || a.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted) return true;
        a.RunOnUiThread(() => a.RequestPermissions(new[] { Manifest.Permission.RecordAudio }, 1));
        return false;
    }

    protected override void OnPause()
    {
        ProgressStore.Save(); // телефон может закрыть приложение в фоне — прогресс сохраняем сразу
        ReminderReceiver.Schedule();
        base.OnPause();
    }
}
