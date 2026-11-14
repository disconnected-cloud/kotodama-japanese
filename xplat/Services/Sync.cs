using System.IO;
using System.Text.Json;

namespace Matsubakira.Services;

/// <summary>
/// Синхронизация между компьютерами через папку, которую выбрал пользователь (например, папку облачного диска).
/// В папке лежит kotodama-sync.json — общий прогресс. При запуске и закрытии программа сливает его со своим:
/// у карточки побеждает более позднее повторение, лучшие оценки и счётчики — наибольшие, списки — объединение.
/// Сеть не нужна: облачный клиент сам переносит файл.
/// </summary>
public static class Sync
{
    public const string FileName = "kotodama-sync.json";

    public static bool Enabled => ProgressStore.P.SyncFolder is { Length: > 0 } f && Directory.Exists(f);
    public static DateTime LastSync { get; private set; }

    /// <summary>Слить прогресс из папки с текущим и записать общий результат обратно. false — папка недоступна.</summary>
    public static bool Run()
    {
        if (!Enabled || ProgressStore.ReadOnly) return false;
        var path = Path.Combine(ProgressStore.P.SyncFolder, FileName);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllBytes(path), ProgressJson.Default.Progress) is { } other)
            {
                ProgressStore.Normalize(other);
                Merge(ProgressStore.P, other);
                ProgressStore.Notify();
            }
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, ProgressStore.Serialize());
            File.Move(tmp, path, true);
            LastSync = DateTime.Now;
            return true;
        }
        catch (Exception e) { CrashReport.Warn("Синхронизация", e); return false; }
    }

    /// <summary>Слияние прогресса другого компьютера в mine (настройки этого компьютера не трогаются).</summary>
    public static void Merge(Progress mine, Progress other)
    {
        foreach (var (id, c) in other.Cards)
            if (!mine.Cards.TryGetValue(id, out var my) || c.LastReview > my.LastReview) mine.Cards[id] = c;
        static void Max(Dictionary<string, int> a, Dictionary<string, int> b) { foreach (var (k, v) in b) if (!a.TryGetValue(k, out var x) || v > x) a[k] = v; }
        Max(mine.SpeechBest, other.SpeechBest);
        Max(mine.KanaScore, other.KanaScore);
        Max(mine.History, other.History);
        Max(mine.GameBest, other.GameBest);
        foreach (var (k, v) in other.Mistakes) mine.Mistakes[k] = Math.Max(v, mine.Mistakes.GetValueOrDefault(k));
        mine.GrammarDone.UnionWith(other.GrammarDone);
        mine.Achievements.UnionWith(other.Achievements);
        mine.Flags.UnionWith(other.Flags.Where(f => !f.StartsWith("navfold:") && !f.StartsWith("pending-delete:"))); // вид меню и уборка — свои у каждого компьютера
        foreach (var (d, t) in other.Journal) if (!mine.Journal.TryGetValue(d, out var my) || t.Length > my.Length) mine.Journal[d] = t;
        foreach (var (k, t) in other.Notes) mine.Notes.TryAdd(k, t);
        foreach (var w in other.CustomWords) if (!mine.CustomWords.Any(c => c[0] == w[0] && c[1] == w[1])) mine.CustomWords.Add(w);
        foreach (var b in other.Badges) if (!mine.Badges.Contains(b)) mine.Badges.Add(b);
        mine.Xp = Math.Max(mine.Xp, other.Xp);
        if (other.LastStudy > mine.LastStudy) { mine.LastStudy = other.LastStudy; mine.Streak = other.Streak; }
        else if (other.LastStudy.Date == mine.LastStudy.Date) mine.Streak = Math.Max(mine.Streak, other.Streak);
        mine.StudySessions = Math.Max(mine.StudySessions, other.StudySessions);
        mine.PerfectDays = Math.Max(mine.PerfectDays, other.PerfectDays);
    }
}
