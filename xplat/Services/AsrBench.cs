using System.Diagnostics;
using System.IO;
using System.Text;
using Matsubakira.Views;

namespace Matsubakira.Services;

/// <summary>
/// Замер моделей распознавания (5.2): Kotodama --asr-bench отчёт.md [файлы моделей…]. Без окна, прогресс не меняется.
/// Набор: фразы уроков говорения, озвученные двумя голосами VOICEVOX (правильные ответы), и ловушки — чужая фраза,
/// тишина, шум, бессмысленные слоги. Для каждой модели: доля ошибок по символам (CER) первого прохода, доля зачётов
/// правильных ответов, доля ложных зачётов ловушек в трёх режимах и среднее время полной проверки.
/// </summary>
public static class AsrBench
{
    sealed record Case(string Kind, string TargetJp, string TargetKana, byte[] Wav);

    public static async Task<int> RunAsync(string report, string[] models, int phrases = 30)
    {
        var log = new StringBuilder();
        void Say(string s) { Console.WriteLine(s); log.AppendLine(s); }
        if (!Voicevox.Installed) { Console.Error.WriteLine("Нужен установленный VOICEVOX"); return 2; }
        await JDict.LoadAsync();
        var rnd = new Random(7);
        var items = Db.SpeechLessons.SelectMany(l => l.Items).Where(i => i.Jp.Length is >= 2 and <= 16).DistinctBy(i => i.Jp).OrderBy(_ => rnd.Next()).Take(phrases).ToList();
        uint female = Voicevox.PickStyle(false), male = Voicevox.PickStyle(true);
        byte[] Synth(string text, uint style) => To16k(Voicevox.Synthesize(Voicevox.SpeakableKana(Db.KanaFor(text) ?? text), style) ?? Array.Empty<byte>());
        var cases = new List<Case>();
        foreach (var it in items)
            foreach (var st in new[] { female, male }.Distinct())
                cases.Add(new Case("верно", it.Jp, it.KanaPlain, Synth(it.Jp, st)));
        for (int i = 0; i < items.Count; i++)
        {
            var other = items[(i + items.Count / 2) % items.Count];
            cases.Add(new Case("чужая фраза", items[i].Jp, items[i].KanaPlain, Synth(other.Jp, female)));
        }
        const string Syl = "ぱぴぷぺぽらりるれろもむめぬねへほ";
        for (int i = 0; i < 10; i++)
        {
            var t = items[i % items.Count];
            var junk = new string(Enumerable.Range(0, 4 + rnd.Next(4)).Select(_ => Syl[rnd.Next(Syl.Length)]).ToArray());
            cases.Add(new Case("бессмыслица", t.Jp, t.KanaPlain, Synth(junk, male)));
            cases.Add(new Case("тишина", t.Jp, t.KanaPlain, Noise(rnd, 0.002f)));
            cases.Add(new Case("шум", t.Jp, t.KanaPlain, Noise(rnd, 0.08f)));
        }
        cases.RemoveAll(c => c.Wav.Length < 1000);
        Say($"# Замер моделей распознавания\n\n{DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.ProcessorCount} потоков процессора, {cases.Count} записей " +
            $"({cases.Count(c => c.Kind == "верно")} правильных, {cases.Count(c => c.Kind != "верно")} ловушек). Голоса: VOICEVOX {female} и {male}.\n");
        Say("| Модель | CER первого прохода | Зачёт правильных (easy / normal / strict) | Ложные зачёты (easy / normal / strict) | Время проверки (2 прохода), с |");
        Say("|---|---|---|---|---|");
        var mode = ProgressStore.P.SpeechMode;
        foreach (var model in models)
        {
            Asr.OverridePath = model;
            await Asr.UnloadAsync();
            double cerSum = 0; int cerN = 0;
            var pass = new Dictionary<string, (int ok, int n)>();
            var fake = new Dictionary<string, (int ok, int n)>();
            var times = new List<double>();
            foreach (var c in cases)
            {
                // тишина отсекается до модели (как в приложении) — её зачёт невозможен, но в доле ловушек учитываем
                bool silent = c.Kind == "тишина";
                var sw = Stopwatch.StartNew();
                var a1 = silent ? new AsrResult("", 0, Array.Empty<(string, float)>()) : await Asr.TranscribeDetailedAsync(c.Wav, CancellationToken.None);
                var a2 = silent ? null : await Asr.TranscribeDetailedAsync(c.Wav, CancellationToken.None, c.TargetJp);
                times.Add(sw.Elapsed.TotalSeconds);
                if (c.Kind == "верно")
                {
                    var heard = Kana.ToHiragana(Speech.Normalize(Reading.Of(Speech.Normalize(a1.Text))));
                    var want = Kana.ToHiragana(Speech.Normalize(c.TargetKana));
                    cerSum += 1 - Speech.Similarity(heard, want); cerN++;
                }
                foreach (var m in new[] { "easy", "normal", "strict" })
                {
                    ProgressStore.P.SpeechMode = m;
                    var r = SpeechUi.Decide(a1, a2, c.TargetJp, c.TargetKana);
                    var d = c.Kind == "верно" ? pass : fake;
                    var (ok, n) = d.GetValueOrDefault(m);
                    d[m] = (ok + (r.Ok ? 1 : 0), n + 1);
                }
            }
            ProgressStore.P.SpeechMode = mode;
            string P(Dictionary<string, (int ok, int n)> d) => string.Join(" / ", new[] { "easy", "normal", "strict" }.Select(m => $"{100.0 * d[m].ok / Math.Max(1, d[m].n):0}%"));
            Say($"| {Path.GetFileNameWithoutExtension(model)} | {100 * cerSum / Math.Max(1, cerN):0.0}% | {P(pass)} | {P(fake)} | {times.Average():0.00} |");
        }
        Asr.OverridePath = null;
        File.WriteAllText(report, log.ToString());
        return 0;
    }

    static byte[] To16k(byte[] wav)
    {
        if (wav.Length < 100) return wav;
        var (x, rate) = Platform.Pcm.Decode(wav, true);
        return Recorder.ToWav(Platform.Pcm.ToPcm16(Platform.Pcm.Resample(x, rate, 16000)));
    }

    static byte[] Noise(Random r, float amp)
    {
        var x = new float[16000 * 2];
        for (int i = 0; i < x.Length; i++) x[i] = amp * (float)(r.NextDouble() * 2 - 1);
        return Recorder.ToWav(Platform.Pcm.ToPcm16(x));
    }
}
