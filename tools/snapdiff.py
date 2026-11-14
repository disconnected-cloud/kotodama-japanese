"""
Снапшот-тест интерфейса: сравнивает снимки (--snap) с эталонами из tests/snapshots.

    python tools/snapdiff.py <папка со снимками> <папка эталонов> [порог % экрана] [порог % клетки]

Пиксель считается изменившимся, если цвет отличается больше чем на 24 (из 255) хоть по одному каналу —
так не мешают сглаживание и мелкие различия рендера. Экран «съехал», если изменилось больше порога (по умолчанию 2%).
Экраны без эталона пропускаются: эталон снимается на CI (там другие шрифты) и кладётся в tests/snapshots.
Карта отличий сохраняется рядом со снимком: <имя>.diff.png.
"""
import sys
from pathlib import Path
from PIL import Image, ImageChops

snaps, refs = Path(sys.argv[1]), Path(sys.argv[2])
limit = float(sys.argv[3]) if len(sys.argv) > 3 else 2.0
tile_limit = float(sys.argv[4]) if len(sys.argv) > 4 else 6.0  # порог для одной клетки 8×8
bad = 0
for ref in sorted(refs.glob("*.png")):
    cur = snaps / ref.name
    if not cur.exists():
        print(f"нет снимка  {ref.name}")
        bad += 1
        continue
    a, b = Image.open(ref).convert("RGB"), Image.open(cur).convert("RGB")
    if a.size != b.size:
        print(f"РАЗМЕР      {ref.name}: эталон {a.size}, сейчас {b.size}")
        bad += 1
        continue
    r, g, bl = ImageChops.difference(a, b).split()
    mask = ImageChops.lighter(ImageChops.lighter(r, g), bl).point(lambda v: 255 if v > 24 else 0)
    changed = mask.histogram()[255] * 100.0 / (a.width * a.height)
    # По областям: экран делится на 8×8 клеток. Мелкая, но сосредоточенная правка (другая цифра в «Пройдено 0 из 31»)
    # почти не меняет долю по всему экрану, зато заметна в своей клетке (Е3).
    tw, th = max(1, a.width // 8), max(1, a.height // 8)
    worst = 0.0
    for ty in range(8):
        for tx in range(8):
            tile = mask.crop((tx * tw, ty * th, (tx + 1) * tw, (ty + 1) * th))
            worst = max(worst, tile.histogram()[255] * 100.0 / (tw * th))
    ok = changed <= limit and worst <= tile_limit
    print(f"{'ok        ' if ok else 'СЪЕХАЛО   '}  {ref.name}: изменилось {changed:.2f}% пикселей, худшая область {worst:.1f}%")
    if not ok:
        mask.save(cur.with_suffix(".diff.png"))
        bad += 1
print(f"эталонов: {len(list(refs.glob('*.png')))}, проблем: {bad}")
sys.exit(1 if bad else 0)
