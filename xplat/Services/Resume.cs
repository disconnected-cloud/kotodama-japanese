using Avalonia.Controls;
using Matsubakira.Views;

namespace Matsubakira.Services;

/// <summary>Страница-урок, к которой можно вернуться кнопкой «Продолжить» на главной.</summary>
public interface IResumable
{
    /// <summary>Ключ вида «grammar:6», «text:Лисёнок Гон · глава 1».</summary>
    string ResumeKey { get; }
    /// <summary>Как назвать на главной: «Грамматика · Частицы を・に・へ・で».</summary>
    string ResumeTitle { get; }
}

/// <summary>«Продолжить, где остановился»: последний открытый урок, текст или диалог.</summary>
public static class Resume
{
    public static void Remember(Control page)
    {
        if (page is not IResumable r || ProgressStore.ReadOnly) return;
        var p = ProgressStore.P;
        if (p.LastPlace == r.ResumeKey) return;
        p.LastPlace = r.ResumeKey;
        p.LastPlaceTitle = r.ResumeTitle;
        ProgressStore.Notify();
    }

    /// <summary>Открыть последнее место или null, если его нет (или материал больше не существует).</summary>
    public static Func<Control>? Opener()
    {
        var key = ProgressStore.P.LastPlace;
        int colon = key.IndexOf(':');
        if (colon < 0) return null;
        string kind = key[..colon], arg = key[(colon + 1)..];
        int.TryParse(arg, out var id);
        return kind switch
        {
            "grammar" when Db.Grammar.FirstOrDefault(g => g.Id == id) is { } g => () => new GrammarLessonPage(g),
            "speech" when Db.SpeechLessons.FirstOrDefault(l => l.Id == id) is { } l => () => new SpeechLessonPage(l),
            "dialog" when Db.Dialogs.FirstOrDefault(d => d.Id == id) is { } d => () => new DialogPage(d),
            "unit" when Db.Course.FirstOrDefault(u => u.Id == id) is { } u => () => new UnitPage(u),
            "text" when ReaderPage.Texts.FirstOrDefault(t => t.Title == arg) is { } t => () => new TextPage(t),
            _ => null,
        };
    }
}
