using System.Windows;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Общая очередь повторения: все слова и кандзи, у которых подошёл срок.</summary>
public static class ReviewPage
{
    public static FrameworkElement Create()
    {
        var cards = Db.Words.Where(w => ProgressStore.IsDue(w.Id)).Select(FlashSession.FromWord)
            .Concat(Db.Kanji.Where(k => ProgressStore.IsDue(k.Id)).Select(FlashSession.FromKanji))
            .Shuffle()
            .Take(100);
        return new FlashSession("Повторение", cards);
    }
}
