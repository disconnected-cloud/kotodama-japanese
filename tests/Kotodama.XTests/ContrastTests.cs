using Avalonia.Controls;
using Avalonia.Media;
using Matsubakira;

namespace Kotodama.Tests;

/// <summary>7.2: текст всех уровней (основной, Muted, Faint) читается на всех фонах во всех темах — контраст не ниже 4,5:1 (WCAG AA).</summary>
public class ContrastTests
{
    static double Lum(Color c)
    {
        static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
    static double Ratio(Color a, Color b) { double la = Lum(a), lb = Lum(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TextIsReadableOnEverySurface(bool light, bool night)
    {
        var r = new ResourceDictionary();
        App.ApplyPalette(r, light, "sakura", night);
        Color C(string k) => ((ISolidColorBrush)r[k]!).Color;
        foreach (var fg in new[] { "Text", "Muted", "Faint" })
            foreach (var bg in new[] { "Bg", "Surface", "Surface2" })
                Assert.True(Ratio(C(fg), C(bg)) >= 4.5, $"{(light ? "светлая" : night ? "ночь" : "тёмная")}: {fg} на {bg} = {Ratio(C(fg), C(bg)):0.00}");
    }
}
