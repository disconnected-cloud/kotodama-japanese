"""
Шрифт иконок «Kotodama Icons» для кроссплатформенной версии.

Segoe Fluent Icons нельзя распространять, и в Linux его нет. Берём глифы из
Microsoft Fluent UI System Icons (MIT) и кладём их на те же коды Private Use Area,
что использует код приложения (U+E710 «добавить», U+E720 «микрофон» и т.д.),
поэтому страницы не нужно переписывать.

Запуск:  python tools/make_icon_font.py <FluentSystemIcons-Regular.ttf> <FluentSystemIcons-Regular.json> xplat/Assets/fonts/KotodamaIcons.ttf
Источник: https://github.com/microsoft/fluentui-system-icons (папка fonts), лицензия MIT.
"""
import json, sys
from fontTools.ttLib import TTFont
from fontTools import subset

# код Segoe → имя иконки Fluent (20-пиксельный рисунок)
MAP = {
    0xE710: "add", 0xE711: "dismiss", 0xE71A: "record_stop", 0xE720: "mic", 0xE721: "search",
    0xE72A: "arrow_right", 0xE72B: "arrow_left", 0xE72C: "arrow_clockwise", 0xE72E: "lock_closed",
    0xE736: "book_open", 0xE73A: "checkbox_checked", 0xE73E: "checkmark", 0xE74D: "delete", 0xE74E: "save",
    0xE765: "keyboard", 0xE767: "speaker_2", 0xE768: "play", 0xE76B: "chevron_left", 0xE76C: "chevron_right",
    0xE7B3: "eye", 0xE7BA: "warning", 0xE7F5: "headphones", 0xE82F: "lightbulb", 0xE896: "arrow_download",
    0xE8AB: "arrow_swap", 0xE8BB: "dismiss", 0xE8C1: "translate", 0xE8D2: "text_font", 0xE8E5: "folder_open",
    0xE8EF: "number_symbol", 0xE916: "timer", 0xE921: "subtract", 0xE922: "maximize", 0xE923: "square_multiple",
    0xE9D9: "quiz_new", 0xEA80: "lightbulb_filament", 0xEBE8: "bug",
    # запас на будущее
    0xE70F: "edit", 0xE713: "settings", 0xE718: "pin", 0xE734: "star", 0xE735: "star", 0xE7C3: "document",
    0xE8A7: "open", 0xE946: "info", 0xE9CE: "question_circle", 0xEA8F: "alert", 0xE7E8: "power", 0xE80F: "home",
}

src, js, out = sys.argv[1], sys.argv[2], sys.argv[3]
names = json.load(open(js, encoding="utf-8"))
font = TTFont(src)
cmap = font.getBestCmap()

wanted = {}
for code, name in MAP.items():
    for size in (20, 24, 16, 28):
        key = f"ic_fluent_{name}_{size}_regular"
        if key in names and names[key] in cmap:
            wanted[code] = cmap[names[key]]
            break
    else:
        print("нет иконки:", name)

# оставляем только нужные глифы
glyphs = sorted(set(wanted.values()))
opts = subset.Options()
opts.glyph_names = True
opts.notdef_outline = True
opts.layout_features = []
opts.name_IDs = []
sub = subset.Subsetter(opts)
sub.populate(glyphs=glyphs)
sub.subset(font)

# новые коды: Segoe-совместимые
for table in font["cmap"].tables:
    if table.isUnicode():
        table.cmap = {code: g for code, g in wanted.items()}
font["cmap"].tables = [t for t in font["cmap"].tables if t.isUnicode()]

name = font["name"]
name.names = []
for nid, text in {0: "Glyphs: Microsoft Fluent UI System Icons (MIT)", 1: "Kotodama Icons", 2: "Regular",
                  3: "Kotodama Icons Regular", 4: "Kotodama Icons", 5: "Version 1.0", 6: "KotodamaIcons-Regular"}.items():
    name.setName(text, nid, 3, 1, 0x409)
    name.setName(text, nid, 1, 0, 0)
font.save(out)
print(f"готово: {len(wanted)} иконок → {out}")
