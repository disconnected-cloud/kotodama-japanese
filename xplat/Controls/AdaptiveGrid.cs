using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Matsubakira.Services;

// Сетка, которая на телефоне сама встаёт в столбик
/// <summary>
/// Сетка из U.Cols на телефоне: если боковые колонки (auto и фиксированные) занимают больше половины ширины,
/// основная колонка «*» сжимается до пары букв в строке — тогда колонки сами встают друг под другом.
/// </summary>
public sealed class AdaptiveGrid : Grid
{
    public bool Stacked;

    protected override Size MeasureOverride(Size available)
    {
        if (!Stacked && !double.IsInfinity(available.Width) && ColumnDefinitions.Count >= 2 && RowDefinitions.Count <= 1
            && ColumnDefinitions.Any(c => c.Width.IsStar) && Children.All(c => GetRow(c) == 0 && GetColumnSpan(c) == 1))
        {
            double side = 0, star = 0;
            for (int i = 0; i < ColumnDefinitions.Count; i++)
            {
                var w = ColumnDefinitions[i].Width;
                if (w.IsAbsolute) { side += w.Value; continue; }
                double m = 0;
                foreach (var c in Children)
                    if (GetColumn(c) == i && c.IsVisible) { c.Measure(Size.Infinity); m = Math.Max(m, c.DesiredSize.Width); }
                if (w.IsAuto) side += m; else star += m;
            }
            // всё помещается в строку — оставляем как есть; иначе в столбик, если основной колонке остаётся меньше 55% ширины
            if (side + star > available.Width && available.Width - side < available.Width * 0.55) U.Stackify(this);
            Stacked = true; // решение принимается один раз: на телефоне ширина не меняется
        }
        return base.MeasureOverride(available);
    }
}

/// <summary>Сетка плиток: число колонок — по доступной ширине (не меньше MinTile на плитку, не больше MaxColumns).</summary>
public sealed class TileGrid : Avalonia.Controls.Primitives.UniformGrid
{
    public double MinTile = 160;
    public int MaxColumns = 4;

    protected override Size MeasureOverride(Size available)
    {
        if (!double.IsInfinity(available.Width))
            Columns = Math.Clamp((int)(available.Width / MinTile), 1, MaxColumns);
        return base.MeasureOverride(available);
    }
}
