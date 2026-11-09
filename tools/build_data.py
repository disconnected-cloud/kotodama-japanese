import json, os, sys, gzip, difflib, fugashi
proj, jm = sys.argv[1], sys.argv[2]
D = lambda f: os.path.join(proj, "Data", f)
load = lambda f: json.load(open(D(f), encoding="utf8"))
tagger = fugashi.Tagger()
def hira(s): return "".join(chr(ord(c) - 0x60) if "\u30a1" <= c <= "\u30f6" else c for c in s)
PUNCT = set("。、！？!?「」・…　 ")
def has_kanji(s): return any(0x4e00 <= ord(c) <= 0x9fff for c in s)
def clean(s): return "".join(c for c in s if c not in PUNCT and c != "＿")

def segment(jp, kana=None):
    """Возвращает кану с пробелами между словами; частицы は/へ/を записаны фонетически (わ/え/お)."""
    words = []  # список [reading, kind] ; kind: 'w' слово, 'p' пунктуация
    prev_pos = None
    prev_kanji = False
    for t in tagger(jp):
        s, f = t.surface, t.feature
        pos1, pos2 = f.pos1, f.pos2
        if pos1 in ("補助記号", "空白"):
            if words: words[-1][0] += s
            prev_pos = pos1; continue
        r = hira(f.kana) if f.kana and f.kana != "*" else hira(s)
        if pos1 == "助詞" and s in ("は", "へ", "を") and pos2 in ("係助詞", "格助詞"):
            r = {"は": "わ", "へ": "え", "を": "お"}[s]
        attach = words and (
            (pos1 == "助動詞" and prev_pos in ("動詞", "形容詞", "助動詞")) or
            pos1 == "接尾辞" or
            (pos1 == "助詞" and pos2 == "接続助詞" and s in ("て", "で") and prev_pos in ("動詞", "形容詞", "助動詞")) or
            (s == "そう" and pos1 == "形状詞" and prev_pos in ("動詞", "形容詞")) or
            (pos1 == "名詞" and prev_pos == "名詞" and has_kanji(s) and prev_kanji))
        if attach: words[-1][0] += r
        else: words.append([r, pos1])
        prev_kanji = has_kanji(s)
        prev_pos = pos1
    seg = " ".join(w[0] for w in words)
    if kana:
        # Переносим границы слов на «наше» чтение (на случай расхождений вроде にっぽん/にほん)
        R = "".join(clean(w[0]) for w in words)
        K = clean(hira(kana))
        if R != K and K:
            fixed = {"わ": "は", "え": "へ", "お": "を"}
            sm = difflib.SequenceMatcher(None, R, K, autojunk=False)
            mapping = {}
            for a, b, n in sm.get_matching_blocks():
                for i in range(n + 1): mapping[a + i] = b + i
            out, pos = [], 0
            cum = 0
            bounds = []
            for w in words:
                cum += len(clean(w[0])); bounds.append(cum)
            for bnd in bounds:
                kb = mapping.get(bnd)
                if kb is None:  # ближайшая сопоставленная граница
                    cands = [k for k in mapping if k <= bnd]
                    kb = mapping[max(cands)] if cands else pos
                piece = K[pos:kb]
                if piece: out.append(piece)
                pos = kb
            if pos < len(K): out.append(K[pos:])
            # частицы снова делаем фонетическими
            out = [{"は": "わ", "へ": "え", "を": "お"}.get(p, p) for p in out]
            seg = " ".join(out) + ("。" if jp.endswith("。") else "")
    return seg.strip().replace("にっぽん", "にほん")

texts = {}
def add(jp, kana=None):
    if jp and jp not in texts: texts[jp] = segment(jp, kana)
for f in ("grammar.json", "grammar_n4.json", "grammar_n3.json"):
    for l in load(f):
        for p in l["points"]:
            for ex in p["ex"]: add(ex[0], ex[1])
        for q in l["quiz"]:
            add(q[0].replace("＿＿", q[1]).replace("＿", q[1]))
for u in load("course.json"):
    for ex in u["examples"]: add(ex[0], ex[1])
for vf in ("vocab_plus.json", "vocab_n3.json"):
  if os.path.exists(D(vf)):
    for topic in load(vf).values():
        for w in topic:
            if len(w) > 3 and w[3]: add(w[3])
json.dump(texts, open(D("romaji.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
print("romaji entries:", len(texts))
for k in list(texts)[:6]: print("  ", k, "=>", texts[k])

# ─────────── Словарь JMdict (рус.) ───────────
d = json.load(open(jm, encoding="utf8"))
rows = []
for e in d["words"]:
    senses = []
    for s in e["sense"]:
        g = [x["text"] for x in s["gloss"] if x.get("lang") == "rus"]
        if g: senses.append(", ".join(g[:4]))
        if len(senses) >= 3: break
    if not senses: continue
    kanji = "、".join(k["text"] for k in e["kanji"][:2])
    kana = e["kana"][0]["text"] if e["kana"] else ""
    common = any(k.get("common") for k in e["kanji"] + e["kana"])
    ru = "; ".join(senses)
    if len(ru) > 180: ru = ru[:177] + "…"
    rows.append((0 if common else 1, kanji, kana, ru.replace("\t", " ").replace("\n", " ")))
rows.sort(key=lambda r: r[0])
with gzip.open(D("dict.tsv.gz"), "wt", encoding="utf8", compresslevel=9) as f:
    for c, k, kn, ru in rows: f.write(f"{1 - c}\t{k}\t{kn}\t{ru}\n")
print("dict entries:", len(rows), "common:", sum(1 for r in rows if r[0] == 0), "size KB:", os.path.getsize(D("dict.tsv.gz")) // 1024)
