# Кандзи уровня N3: иероглифы из частотных слов (vocab_plus + vocab_n3), которых нет в N5/N4.
# Чтения и число черт — KANJIDIC2 (EDRDG, CC BY-SA 4.0), значение и примеры — из слов курса с русским переводом.
# python -X utf8 tools/build_kanji_n3.py <проект> <kanjidic2-all.json> [кол-во]
import json, os, sys, collections, re
proj, kd = sys.argv[1], sys.argv[2]
LIMIT = int(sys.argv[3]) if len(sys.argv) > 3 else 150
LV = sys.argv[4] if len(sys.argv) > 4 else "n3"   # n2: знаки из слов N2, которых нет в N5–N3
D = lambda f: os.path.join(proj, "Data", f)
load = lambda f: json.load(open(D(f), encoding="utf8"))
is_kanji = lambda c: 0x4e00 <= ord(c) <= 0x9fff
known = {k[0] for f in ("kanji.json", "kanji_n4.json") + (("kanji_n3.json",) if LV == "n2" else ()) for k in load(f)}
words = []
for f in (("vocab_n2.json",) if LV == "n2" else ("vocab_plus.json", "vocab_n3.json")):
    for topic in load(f).values():
        for w in topic: words.append((w[0], w[1] or w[0], w[2]))
count = collections.Counter(); ex = collections.defaultdict(list)
for i, (jp, kana, ru) in enumerate(words):
    for c in set(jp):
        if is_kanji(c) and c not in known:
            count[c] += 1600 - i            # раньше в частотном списке — важнее
            if len(ex[c]) < 3: ex[c].append([jp, kana, ", ".join(x.strip() for x in ru.split(";")[0].split(",")[:2]).strip(" ,")])
if kd.endswith(".xml.gz"):
    # Оригинальный XML с сайта EDRDG → та же структура, что и в jmdict-simplified
    import gzip, xml.etree.ElementTree as ET
    dic = {}
    for ch in ET.parse(gzip.open(kd)).getroot().iter("character"):
        lit = ch.findtext("literal")
        rs = [{"type": r.get("r_type"), "value": r.text} for r in ch.iter("reading")]
        sc = [int(x.text) for x in ch.iter("stroke_count")]
        dic[lit] = {"literal": lit, "readingMeaning": {"groups": [{"readings": rs}]}, "misc": {"strokeCounts": sc}}
else:
    dic = {e["literal"]: e for e in json.load(open(kd, encoding="utf8"))["characters"]}
def readings(e, t):
    out = []
    for g in (e.get("readingMeaning") or {}).get("groups", []):
        out += [r["value"] for r in g.get("readings", []) if r["type"] == t]
    return out
res = []
for c, _ in count.most_common():
    if len(res) >= LIMIT: break
    e = dic.get(c)
    if not e or not ex[c]: continue
    on = "、".join(readings(e, "ja_on")[:3]); kun = "、".join(r for r in readings(e, "ja_kun")[:3] if not r.startswith("-"))
    strokes = e["misc"]["strokeCounts"][0] if e["misc"].get("strokeCounts") else 0
    # значение: перевод самостоятельного слова из одного знака, иначе — первого слова-примера
    manual = json.load(open(os.path.join(proj, "tools", f"kanji_{LV}_meanings.json"), encoding="utf8")) if os.path.exists(os.path.join(proj, "tools", f"kanji_{LV}_meanings.json")) else {}
    meaning = manual.get(c) or next((w[2] for w in ex[c] if w[0] == c), None) or f"в слове {ex[c][0][0]}: {ex[c][0][2]}"
    res.append([c, on, kun, meaning, strokes, ex[c][:2]])
json.dump(res, open(D(f"kanji_{LV}.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
print("kanji", LV, len(res))
for r in res: print(r[0], "|", r[3], "|", r[5][0][0])
