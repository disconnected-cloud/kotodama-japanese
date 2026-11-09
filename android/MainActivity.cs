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
        ProgressStore.Load();
        return base.CustomizeAppBuilder(builder);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Микрофон (проверка произношения) и уведомления (слово дня) — разрешения спрашиваем при первом запуске
        var need = new List<string>();
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted) need.Add(Manifest.Permission.RecordAudio);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            need.Add(Manifest.Permission.PostNotifications);
        if (need.Count > 0) RequestPermissions(need.ToArray(), 1);
    }

    protected override void OnPause()
    {
        ProgressStore.Save(); // телефон может закрыть приложение в фоне — прогресс сохраняем сразу
        base.OnPause();
    }
}
