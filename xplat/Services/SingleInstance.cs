using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Matsubakira.Services;

/// <summary>
/// Одна копия программы на одну папку данных: две копии держат прогресс в памяти и по очереди перезаписывают
/// progress.json — изменения одной терялись. Имя мьютекса зависит от пути папки данных, поэтому портативная
/// копия на флешке не мешает установленной. Вторая копия просит первую показать окно и завершается.
/// </summary>
public static class SingleInstance
{
    static Mutex? _mutex;

    static string Key(string folder)
    {
        var norm = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (OperatingSystem.IsWindows()) norm = norm.ToLowerInvariant();
        return "Kotodama-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
    }

    /// <summary>
    /// true — эта копия единственная. waitMs — сколько ждать, пока прежняя копия закроется
    /// (перезапуск из настроек запускает новую копию до выхода старой).
    /// </summary>
    public static bool Acquire(string folder, int waitMs)
    {
        var key = Key(folder);
        try
        {
            _mutex = new Mutex(false, (OperatingSystem.IsWindows() ? @"Local\" : "") + key);
            bool own;
            try { own = _mutex.WaitOne(waitMs); }
            catch (AbandonedMutexException) { own = true; } // прежняя копия упала, не отпустив мьютекс
            if (!own) { _mutex.Dispose(); _mutex = null; }
            return own;
        }
        catch (Exception e)
        {
            // Именованный мьютекс недоступен (редкие окружения) — лучше запустить, чем не запустить вовсе
            CrashReport.Warn("Проверка второй копии", e);
            return true;
        }
    }

    /// <summary>Попросить уже запущенную копию показать окно. false — она не ответила.</summary>
    public static bool SignalFirst(string folder)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", Key(folder), PipeDirection.Out);
            c.Connect(1500);
            c.WriteByte(1);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Слушать просьбы второй копии (в фоне, до выхода программы).</summary>
    public static void Listen(string folder, Action show)
    {
        var key = Key(folder);
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(key, PipeDirection.In, 1);
                    s.WaitForConnection();
                    if (s.ReadByte() == 1) show();
                }
                catch (Exception e) { CrashReport.Warn("Сигнал второй копии", e); Thread.Sleep(2000); }
            }
        }) { IsBackground = true, Name = "single-instance" };
        t.Start();
    }
}
