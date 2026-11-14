using System.Diagnostics;
using System.IO;

namespace Matsubakira.Platform;

/// <summary>
/// Системный синтез речи — запасной голос, когда VOICEVOX не установлен. Всё офлайн.
/// Windows: голоса SAPI из языкового пакета (японский — Haruka/Ayumi/Ichiro, русский — Irina/Pavel), в т.ч. голоса OneCore.
/// Linux: японский — Open JTalk (если установлен вместе со словарём и голосом), русский — RHVoice или espeak-ng.
/// Вызовы Windows идут через COM по имени, поэтому сборка для Linux от них не зависит.
/// </summary>
public static class SystemVoice
{
    /// <summary>Название японского голоса системы или null, если его нет (результат запоминается).</summary>
    public static string? JaName => _ja ??= Find(Lang.Ja) ?? "";
    public static string? RuName => _ru ??= Find(Lang.Ru) ?? "";
    public static bool HasJa => !string.IsNullOrEmpty(JaName);
    public static bool HasRu => !string.IsNullOrEmpty(RuName);
    static string? _ja, _ru;

    enum Lang { Ja, Ru }

    /// <summary>Японская речь в WAV или null.</summary>
    public static byte[]? SpeakJa(string text, bool slow) => HasJa ? Speak(Lang.Ja, text, slow) : null;
    public static byte[]? SpeakRu(string text) => HasRu ? Speak(Lang.Ru, text, false) : null;

    /// <summary>Как добавить японский голос — для сообщения пользователю.</summary>
    public static string HowToInstallJa => OperatingSystem.IsWindows()
        ? "Японский голос Windows: Параметры → Время и язык → Язык и регион → Добавить язык → Японский (с речевым пакетом). Или установи VOICEVOX в Настройках → Голоса."
        : "Установи офлайн-голоса VOICEVOX в Настройках → Голоса (или пакеты open-jtalk, open-jtalk-mecab-naist-jdic и hts-voice-nitech-jp-atr503-m001).";

    static string? Find(Lang lang)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Sapi.Find(lang == Lang.Ja ? "411" : "419");
            if (OperatingSystem.IsLinux()) return lang == Lang.Ja ? (JTalk.Ready ? "open_jtalk" : null) : RuLinux.Engine;
        }
        catch (Exception e) { Services.CrashReport.Warn("Системный голос", e); }
        return null;
    }

    static byte[]? Speak(Lang lang, string text, bool slow)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Sapi.Speak(lang == Lang.Ja ? "411" : "419", text, slow);
            if (OperatingSystem.IsLinux()) return lang == Lang.Ja ? JTalk.Speak(text, slow) : RuLinux.Speak(text);
        }
        catch (Exception e) { Services.CrashReport.Warn("Системный голос: синтез", e); }
        return null;
    }

    /// <summary>SAPI 5 через COM. Голоса OneCore (их видит «Параметры → Речь») подключаются отдельной категорией.</summary>
    static class Sapi
    {
        const string OneCore = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
        static readonly object Lock = new();

        static dynamic? Token(string lcid)
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (t == null) return null;
            dynamic voice = Activator.CreateInstance(t)!;
            dynamic list = voice.GetVoices("Language=" + lcid, "");
            if (list.Count > 0) return list.Item(0);
            var ct = Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory");
            if (ct == null) return null;
            dynamic cat = Activator.CreateInstance(ct)!;
            cat.SetId(OneCore, false);
            dynamic more = cat.EnumerateTokens("Language=" + lcid, "");
            return more.Count > 0 ? more.Item(0) : null;
        }

        public static string? Find(string lcid)
        {
            lock (Lock)
            {
                var tok = Token(lcid);
                return tok == null ? null : (string)tok.GetDescription(0);
            }
        }

        public static byte[]? Speak(string lcid, string text, bool slow)
        {
            lock (Lock)
            {
                var tok = Token(lcid);
                if (tok == null) return null;
                var tmp = Path.Combine(Path.GetTempPath(), "kotodama-sapi-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
                try
                {
                    dynamic voice = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice")!)!;
                    voice.Voice = tok;
                    voice.Rate = slow ? -3 : 0;
                    dynamic fmt = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpAudioFormat")!)!;
                    fmt.Type = 22; // SAFT22kHz16BitMono
                    dynamic fs = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream")!)!;
                    fs.Format = fmt;
                    fs.Open(tmp, 3, false); // SSFMCreateForWrite
                    voice.AudioOutputStream = fs;
                    voice.Speak(System.Security.SecurityElement.Escape(text), 8); // SVSFIsXML: текст экранирован
                    fs.Close();
                    return File.Exists(tmp) ? File.ReadAllBytes(tmp) : null;
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
        }
    }

    /// <summary>Open JTalk из пакетов дистрибутива (Debian/Ubuntu: open-jtalk, open-jtalk-mecab-naist-jdic, hts-voice-*).</summary>
    static class JTalk
    {
        static readonly string[] Dics = { "/var/lib/mecab/dic/open-jtalk/naist-jdic", "/usr/share/open-jtalk/dic", "/usr/lib/open_jtalk/dic" };
        static readonly string[] VoiceDirs = { "/usr/share/hts-voice", "/usr/share/open-jtalk/voice", "/usr/lib/open_jtalk/voice" };
        static string? Dic => Dics.FirstOrDefault(Directory.Exists);
        static string? Voice => VoiceDirs.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.htsvoice", SearchOption.AllDirectories)).OrderBy(f => f.Contains("normal") ? 0 : 1).FirstOrDefault();
        public static bool Ready => Which("open_jtalk") != null && Dic != null && Voice != null;

        public static byte[]? Speak(string text, bool slow)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "kotodama-jt-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                File.WriteAllText(tmp + ".txt", text);
                Run("open_jtalk", "-x", Dic!, "-m", Voice!, "-r", slow ? "0.8" : "1.0", "-ow", tmp + ".wav", tmp + ".txt");
                return File.Exists(tmp + ".wav") ? File.ReadAllBytes(tmp + ".wav") : null;
            }
            finally { foreach (var f in new[] { tmp + ".txt", tmp + ".wav" }) try { File.Delete(f); } catch { } }
        }
    }

    /// <summary>Русский голос Linux: RHVoice (естественнее) или espeak-ng (есть почти везде).</summary>
    static class RuLinux
    {
        public static string? Engine => Which("RHVoice-test") != null ? "RHVoice" : Which("espeak-ng") != null ? "espeak-ng" : null;

        public static byte[]? Speak(string text)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "kotodama-ru-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            try
            {
                if (Engine == "RHVoice") Run("RHVoice-test", stdin: text, "-p", "Anna", "-o", tmp);
                else Run("espeak-ng", "-v", "ru", "-w", tmp, text);
                return File.Exists(tmp) ? File.ReadAllBytes(tmp) : null;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    static string? Which(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);

    static void Run(string exe, params string[] args) => Run(exe, null, args);

    static void Run(string exe, string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = stdin != null, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
        _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000)) { try { p.Kill(true); } catch { } }
    }
}
