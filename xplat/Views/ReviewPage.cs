using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RadioButton = Matsubakira.Compat.RadioButton;
using CheckBox = Matsubakira.Compat.CheckBox;
using ToggleButton = Matsubakira.Compat.ToggleButton;
using Matsubakira.Controls;
using Matsubakira.Services;

namespace Matsubakira.Views;

/// <summary>Общая очередь повторения: все слова и кандзи, у которых подошёл срок.</summary>
public static class ReviewPage
{
    public static Control Create()
    {
        var cards = Db.Words.Where(w => ProgressStore.IsDue(w.Id)).Select(FlashSession.FromWord)
            .Concat(Db.Kanji.Where(k => ProgressStore.IsDue(k.Id)).Select(FlashSession.FromKanji))
            .Shuffle()
            .Take(100);
        return new FlashSession("Повторение", cards);
    }
}
