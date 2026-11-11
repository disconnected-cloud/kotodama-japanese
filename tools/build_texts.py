# Разбор текстов читалки MeCab/UniDic: токены [поверхность, чтение (хирагана), словарная форма] + латиница предложений.
# python -X utf8 tools/build_texts.py <проект>
import json, os, sys, fugashi
proj = sys.argv[1]
D = lambda f: os.path.join(proj, "Data", f)
hira = lambda s: "".join(chr(ord(c) - 0x60) if "ァ" <= c <= "ヶ" else c for c in s)
has_kanji = lambda s: any(0x4e00 <= ord(c) <= 0x9fff for c in s)
tagger = fugashi.Tagger()
src = json.load(open(os.path.join(proj, "tools", "texts_src.json"), encoding="utf8"))
out = []
for t in src:
    sents = []
    for jp, ru in t["sentences"]:
        toks = []
        for w in tagger(jp):
            f = w.feature
            reading = hira(f.kana) if f.kana and f.kana != "*" else w.surface
            lemma = f.orthBase if f.orthBase and f.orthBase != "*" else w.surface
            reading = {"私": "わたし", "何": "なに", "日本": "にほん", "明日": "あした", "宮城": "ぐうじょう", "下": "した", "箱": "はこ",
                       "宝物": "たからもの", "むし暑い": "むしあつい", "七": "しち", "四": "よ"}.get(w.surface, reading)
            toks.append([w.surface, reading if has_kanji(w.surface) else "", lemma, f.pos1])
        sents.append({"jp": jp, "ru": ru, "tokens": toks})
    item = {"title": t["title"], "level": t["level"], "sentences": sents, "questions": t["questions"]}
    if t.get("book"): item["book"] = t["book"]
    out.append(item)
json.dump(out, open(D("texts.json"), "w", encoding="utf8"), ensure_ascii=False, separators=(",", ":"))
print("texts:", len(out), "tokens:", sum(len(s["tokens"]) for t in out for s in t["sentences"]))
