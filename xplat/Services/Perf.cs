using System.Diagnostics;

namespace Matsubakira.Services;

/// <summary>
/// Замер запуска: метки времени от старта процесса (Program.Main / Activity) до первого кадра и фоновых загрузок.
/// Метки ничего не стоят; отладочный флаг --perf out.txt сохраняет их вместе с памятью и выходит.
/// </summary>
public static class Perf
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly double Offset = StartOffset();
    static readonly List<(string name, double ms)> _marks = new();

    // сколько процесс уже работал до первого обращения к Perf (загрузка рантайма)
    static double StartOffset()
    {
        try { return Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds); }
        catch { return 0; }
    }

    public static double Now => Offset + Clock.Elapsed.TotalMilliseconds;

    public static void Mark(string name) { lock (_marks) _marks.Add((name, Now)); }

    public static string Report()
    {
        lock (_marks)
        {
            var lines = _marks.OrderBy(m => m.ms).Select(m => $"{m.ms,8:0} мс  {m.name}").ToList();
            lines.Add($"память: управляемая {GC.GetTotalMemory(true) / 1048576.0:0.0} МБ, процесс {Environment.WorkingSet / 1048576.0:0} МБ");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Завершается, когда фоновые загрузки после первого кадра готовы.</summary>
    public static Task WarmedUp { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Тяжёлое, что не нужно для первого кадра: словарь (~0,5–1,5 с), акценты.
    /// Запускается после того, как окно показано, и с паузой — чтобы не спорить с анимацией появления за процессор.
    /// </summary>
    public static void WarmUpAfterFirstFrame()
    {
        Mark("первый кадр");
        WarmedUp = Task.Run(async () =>
        {
            await Task.Delay(OperatingSystem.IsAndroid() ? 1500 : 600);
            await JDict.LoadAsync(); Mark("словарь загружен");
            Pitch.Preload(); Mark("акценты загружены");
        });
        // озвучка сегодняшней сессии «Учиться», если она ещё не пройдена (данные прогресса читаем в потоке интерфейса)
        if (!Environment.GetCommandLineArgs().Skip(1).Any())
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (!ProgressStore.P.Flags.Contains("study:" + DateTime.Today.ToString("yyyy-MM-dd")))
                    Views.StudyPage.Prefetch(Math.Clamp(ProgressStore.P.StudyMode, 0, 2));
            }, TimeSpan.FromSeconds(OperatingSystem.IsAndroid() ? 8 : 4));
    }
}
