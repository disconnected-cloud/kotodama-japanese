using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Matsubakira.Services;

/// <summary>
/// Иконка приложения со значком числа карточек к повторению — для панели задач и трея.
/// Рисуется на лету (иероглиф 言 + кружок с числом), в светлом и тёмном варианте под тему панели.
/// </summary>
public static class Badge
{
    static int _lastCount = -1;
    static bool _lastLight;

    /// <summary>Тема панели задач Windows: светлая или тёмная (в Linux берём тему приложения наоборот — панели обычно тёмные).</summary>
    public static bool LightTaskbar()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    /// <summary>PNG-картинка иконки size×size.</summary>
    public static byte[] Render(int count, bool light, int size)
    {
        var accent = Color.Parse(light ? "#D9487A" : "#F07AA0");
        var root = new Grid { Width = size, Height = size };
        // подложка — скруглённый квадрат, иероглиф в цвете акцента
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22), Margin = new Thickness(size * 0.04),
            Background = new SolidColorBrush(Color.Parse(light ? "#1C1B1F" : "#16171C")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)), BorderThickness = new Thickness(Math.Max(1, size / 32.0)),
        });
        root.Children.Add(new TextBlock
        {
            Text = "言", FontSize = size * 0.62, Foreground = new SolidColorBrush(accent),
            FontFamily = TryFont("JpSerif"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, size * 0.04),
        });
        if (count > 0)
        {
            var text = count > 99 ? "99+" : count.ToString();
            double d = size * (text.Length > 2 ? 0.62 : 0.52);
            root.Children.Add(new Border
            {
                Width = Math.Max(d, size * 0.3 * text.Length), Height = d, CornerRadius = new CornerRadius(d / 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.Parse("#E5484D")),
                BorderBrush = new SolidColorBrush(Color.Parse(light ? "#F3F3F3" : "#202020")), BorderThickness = new Thickness(Math.Max(1, size / 24.0)),
                Child = new TextBlock
                {
                    Text = text, FontSize = d * (text.Length > 2 ? 0.48 : 0.62), FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        using var rtb = new RenderTargetBitmap(new PixelSize(size, size));
        rtb.Render(root);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }

    static FontFamily TryFont(string key) { try { return U.R<FontFamily>(key); } catch { return FontFamily.Default; } }

    /// <summary>Обновить иконку окна (панель задач) и трея, если число карточек или тема изменились.</summary>
    public static void Update(Window w)
    {
        int due = ProgressStore.P.Cards.Values.Count(c => c.Due <= DateTime.Now);
        bool light = LightTaskbar();
        if (due == _lastCount && light == _lastLight) return;
        _lastCount = due; _lastLight = light;
        try
        {
            var big = Render(due, light, 64);
            w.Icon = new WindowIcon(new MemoryStream(big));
            Tray.SetIcon(Render(due, light, 32), due);
        }
        catch { }
    }
}
