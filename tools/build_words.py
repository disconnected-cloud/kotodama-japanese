# ~1000 новых слов: частотность по корпусу Tatoeba (MeCab/UniDic), перевод из JMdict (рус.), пример-фраза с переводом из Tatoeba.
# Запуск: python -X utf8 tools/build_words.py <проект> <папка tatoeba> <jmdict-rus.json> [кол-во]
import bz2, json, os, sys, collections, re
import fugashi
proj, tdir, jm = sys.argv[1], sys.argv[2], sys.argv[3]
LIMIT = int(sys.argv[4]) if len(sys.argv) > 4 else 1000
D = lambda f: os.path.join(proj, "Data", f)
hira = lambda s: "".join(chr(ord(c) - 0x60) if "ァ" <= c <= "ヶ" else c for c in s)

def tsv(name):
    with bz2.open(os.path.join(tdir, name), "rt", encoding="utf8") as f:
        for line in f:
            p = line.rstrip("\n").split("\t")
            yield p
jp = {p[0]: p[2] for p in tsv("jpn_sentences.tsv.bz2") if len(p) >= 3}
ru = {p[0]: p[2] for p in tsv("rus_sentences.tsv.bz2") if len(p) >= 3}
pairs = {}
for p in tsv("jpn-rus_links.tsv.bz2"):
    if len(p) >= 2 and p[0] in jp and p[1] in ru and p[0] not in pairs: pairs[p[0]] = ru[p[1]]
print("jp sentences:", len(jp), "pairs jp-ru:", len(pairs))

tagger = fugashi.Tagger()
freq = collections.Counter()
orth = collections.defaultdict(collections.Counter)   # как слово реально пишут в корпусе
examples = collections.defaultdict(list)
POS_OK = {"名詞", "動詞", "形容詞", "形状詞", "副詞", "感動詞", "代名詞"}
for sid, text in jp.items():
    seen = set()
    for t in tagger(text):
        f = t.feature
        if f.pos1 not in POS_OK or f.pos2 in ("固有名詞", "数詞") or not f.lemma or f.lemma == "*": continue
        lemma = f.lemma.split("-")[0]
        key = (lemma, hira(f.lForm or ""), f.pos1)
        freq[key] += 1
        if f.orthBase and f.orthBase != "*": orth[key][f.orthBase] += 1
        if sid in pairs and key not in seen and 5 <= len(text) <= 22 and len(pairs[sid]) <= 70:
            seen.add(key)
            ex = examples[key]
            if len(ex) < 12: ex.append(sid)
print("lemmas:", len(freq))

# JMdict: форма → [(kana, ru, common)]
d = json.load(open(jm, encoding="utf8"))
dic = collections.defaultdict(list)
for e in d["words"]:
    senses = []
    for s in e["sense"]:
        g = [x["text"] for x in s["gloss"] if x.get("lang") == "rus"]
        if g: senses.append(", ".join(g[:3]))
        if len(senses) >= 2: break
    if not senses: continue
    def clean(g):
        g = re.sub(r"\{[^}]*\}", "", g)                       # {～の}
        g = re.sub(r"\(\((тж|см|ср)\.\)[^)]*\)", "", g)      # ((тж.) 甘い)
        g = re.sub(r"\((тж|см|ср|уст|кн|прост|разг|диал|сокр)\.\)\s*[^\s,;]*[\u3040-\u30ff\u4e00-\u9fff]+[^\s,;]*", "", g)  # (см.) じじい【爺】
        g = re.sub(r"\((уст|кн|прост|разг|диал|сокр|вежл|ласк|бран|груб|шутл|ирон|редко)\.\)", "", g)
        g = re.sub(r"\(ср\.\).*$", "", g)
        g = re.sub(r"\((кн|уст|разг|прост)\.\s*см\.\)", "", g)
        g = re.sub(r"^\s*\d+[.)]\s*:?", "", g)                 # «1) …» в начале
        g = re.sub(r"\s*\d+[.)]\s*", "; ", g)                   # внутренние номера → «;»
        g = re.sub(r"[\u3040-\u30ff\u4e00-\u9fff【】]+", "", g)  # японские вставки в переводе
        g = re.sub(r"\(\s*\)|\[\s*\]", "", g)
        g = re.sub(r"\s*;\s*;+", ";", g)
        return re.sub(r"\s+", " ", g).strip(" :;,.")
    # Пропускаем служебные значения в начале статьи («(после числ.) порция», «(связ.:) …»)
    SERVICE = ("(после", "(связ", "(служебное", "(инфикс", "(ист.", "(суф", "(преф", "(счётн", "(частица", "(в сочет")
    good = [x for x in (clean(z) for z in senses) if x and not x.startswith(SERVICE)] or [clean(senses[0])]
    s1 = good[0]
    gloss = s1 if len(good) == 1 or len(s1) > 40 else s1 + "; " + good[1]
    gloss = re.sub(r"\s*[,;]\s*(?=[,;])", "", gloss).strip(" ,;")
    if len(gloss) > 70:
        cut = max(gloss.rfind(",", 0, 70), gloss.rfind(";", 0, 70))
        gloss = gloss[:cut] if cut > 15 else gloss[:70]
    uk = any("uk" in (sn.get("misc") or []) for sn in e["sense"][:1])
    common = any(k.get("common") for k in e["kanji"] + e["kana"])
    kanas = [k["text"] for k in e["kana"]]
    for form in [k["text"] for k in e["kanji"]] + kanas:
        dic[form].append((kanas, gloss, common, uk))

existing = set()
for f in ("vocab.json", "vocab_n4.json"):
    for topic in json.load(open(D(f), encoding="utf8")).values():
        for w in topic: existing.add(w[0]); existing.add(w[1] or w[0])

OVERRIDE = {
    "こと": "дело, вещь; факт", "前": "перед; впереди; раньше", "時": "время; когда (в момент)", "もう": "уже; ещё (один)",
    "よう": "вид, образ; как будто", "方": "сторона; способ; (вежл.) человек", "日": "день; солнце", "ところ": "место; момент",
    "中": "внутри; середина; среди", "もの": "вещь, предмет", "ため": "для, ради; из-за", "君": "ты (к младшим, близким)",
    "年": "год", "金": "деньги; золото", "上": "верх; на, над", "下": "низ; под", "後": "после; позже; сзади", "間": "промежуток; между; пока",
    "人": "человек", "者": "человек (суффикс)", "気": "дух, настроение", "手": "рука", "目": "глаз", "家": "дом; семья", "道": "дорога, путь",
    "事": "дело, вещь", "物": "вещь, предмет", "話": "разговор, рассказ", "声": "голос", "顔": "лицо", "心": "сердце, душа",
    "とても": "очень", "よく": "хорошо; часто", "まだ": "ещё (не)", "すぐ": "сразу, сейчас же", "少し": "немного",
}
picked = []
used = set()
for (lemma, reading, pos), n in freq.most_common():
    if len(picked) >= LIMIT: break
    if lemma in existing or lemma in used or re.search(r"[A-Za-z0-9０-９]", lemma): continue
    cands = dic.get(lemma, [])
    best = None
    # Лучшая статья: совпадает чтение (×4), частотная (×2), есть перевод без мусора
    def score(c):
        kanas, gloss, common, uk = c
        return (4 if reading and hira(reading) in [hira(k) for k in kanas] else 0) + (2 if common else 0) + (1 if 3 <= len(gloss) <= 60 else 0)
    if cands: best = max(cands, key=score)
    if not best or len(best[1]) < 2: continue
    kana = best[0][0] if best[0] else hira(reading)
    if reading and hira(reading) in [hira(k) for k in best[0]]: kana = next(k for k in best[0] if hira(k) == hira(reading))
    written = orth[(lemma, reading, pos)].most_common(1)
    form = written[0][0] if written else lemma          # самая частая форма записи в живых текстах
    if re.fullmatch(r"[぀-ゟ]+", form): kana = form
    if form in existing or kana in existing: continue
    ex = examples.get((lemma, reading, pos), [])
    ex.sort(key=lambda sid: len(jp[sid]))
    exj, exr = (jp[ex[0]], pairs[ex[0]]) if ex else ("", "")
    used.add(lemma)
    picked.append((pos, form, kana, OVERRIDE.get(form, best[1]), exj, exr))
print("picked:", len(picked), "with examples:", sum(1 for p in picked if p[4]))

GROUP = {"名詞": "Сущ.", "代名詞": "Сущ.", "動詞": "Глаголы", "形容詞": "Прилаг.", "形状詞": "Прилаг.", "副詞": "Наречия", "感動詞": "Наречия"}
def write(items, fname, prefix=""):
    groups = collections.defaultdict(list)
    for p_ in items: groups[GROUP[p_[0]]].append(p_)
    out = {}
    for g, its in groups.items():
        for i in range(0, len(its), 50):
            out[f"{prefix}{g} {i // 50 + 1}"] = [[lemma, kana if kana != lemma else "", ru_, exj, exr] for _, lemma, kana, ru_, exj, exr in its[i:i + 50]]
    json.dump(out, open(D(fname), "w", encoding="utf8"), ensure_ascii=False, indent=0)
    print(fname, "topics:", len(out), "words:", sum(len(v) for v in out.values()))
write(picked[:1000], "vocab_plus.json")
if len(picked) > 1000: write(picked[1000:], "vocab_n3.json", "N3 · ")
