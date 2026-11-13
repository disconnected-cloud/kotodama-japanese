# Обрезает Noto Sans JP до знаков из материалов (+ кана, CJK, полноширинные): python tools/subset_jp_font.py <корень репо>, шрифты NotoSansJP-*.otf — в текущей папке, результат в out/
import glob, gzip, sys
from fontTools.ttLib import TTFont
from fontTools import subset
root = sys.argv[1]
chars = set()
for p in glob.glob(root + '/Data/*'):
    op = gzip.open if p.endswith('.gz') else open
    try:
        with op(p, 'rt', encoding='utf-8', errors='ignore') as f: chars |= set(f.read())
    except Exception as e: print('skip', p, e)
for p in glob.glob(root + '/xplat/**/*.cs', recursive=True):
    chars |= set(open(p, encoding='utf-8', errors='ignore').read())
# всегда: кана, знаки препинания CJK, полноширинные формы, базовая латиница/кириллица
for a, b in [(0x20, 0x24F), (0x400, 0x4FF), (0x2000, 0x206F), (0x2190, 0x21FF), (0x2460, 0x24FF), (0x25A0, 0x25FF),
             (0x3000, 0x30FF), (0x31F0, 0x31FF), (0xFF00, 0xFFEF), (0x4E00, 0x9FFF)]:
    chars |= {chr(c) for c in range(a, b + 1)}
for w in ['Regular', 'Bold']:
    f = TTFont(f'NotoSansJP-{w}.otf')
    cm = f.getBestCmap()
    uni = sorted(ord(c) for c in chars if ord(c) in cm)
    o = subset.Options(); o.layout_features = ['*']; o.name_IDs = ['*']; o.notdef_outline = True; o.hinting = False; o.desubroutinize = False
    s = subset.Subsetter(o); s.populate(unicodes=uni); s.subset(f)
    f.save(f'out/NotoSansJP-{w}.otf'); print(w, len(uni))
