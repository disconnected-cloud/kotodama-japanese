# Обновление Data/romaji.json без полной пересборки: добавляет недостающие предложения курса,
# убирает ключи, которых больше нет в материалах, и исправляет типичные ошибки чтения MeCab.
# python -X utf8 tools/update_romaji.py <проект>      (нужен fugashi + unidic-lite)
import json, os, re, sys
proj = sys.argv[1]
src = open(os.path.join(proj, "tools", "build_data.py"), encoding="utf8").read()
sys.argv = [sys.argv[0], proj, ""]
exec(src[:src.index("texts = {}")])          # segment(), hira(), load() из build_data.py

rom = json.load(open(D("romaji.json"), encoding="utf8"))
want = {}
def add(jp, kana=None):
    if jp and jp not in want: want[jp] = kana
GF = ("grammar.json", "grammar_n4.json", "grammar_n3.json", "grammar_n3b.json", "grammar_more.json", "grammar_n2.json", "grammar_n1.json")
for f in GF:
    for l in load(f):
        for p in l["points"]:
            for ex in p["ex"]: add(ex[0], ex[1])
        for q in l["quiz"]: add(re.sub("＿+", q[1], q[0]))   # пропуск «＿＿＿» — один ответ, а не три
for u in load("course.json"):
    for ex in u["examples"]: add(ex[0], ex[1])
for vf in ("vocab.json", "vocab_n4.json", "vocab_plus.json", "vocab_n3.json", "vocab_n2.json", "vocab_n1.json"):
    for topic in load(vf).values():
        for w in topic:
            if len(w) > 3 and w[3]: add(w[3])
for kf in ("kanji.json", "kanji_n4.json", "kanji_n3.json", "kanji_n2.json", "kanji_n1.json"):
    for k in load(kf):
        for e in k[5]: add(e[0], e[1])

# частые ошибки чтения MeCab (по словам, а не по подстрокам)
TOKEN_FIX = {"わたくし": "わたし", "わたくしたち": "わたしたち", "わたくしたちわ": "わたしたちわ"}
def fix(jp, seg):
    toks = seg.split(" ")
    out = []
    for t in toks:
        core = re.sub(r"[、。！？「」…]+$", "", t); tail = t[len(core):]
        core = TOKEN_FIX.get(core, core)
        if core in ("あす", "あすわ") and "明日" in jp: core = core.replace("あす", "あした")
        out.append(core + tail)
    seg = " ".join(out)
    seg = seg.replace("お ははさん", "お かあさん").replace("お ちちさん", "お とうさん").replace("お あにちゃん", "お にいちゃん").replace("お あねちゃん", "お ねえちゃん")
    seg = seg.replace("あ したは", "あした わ").replace("あ した", "あした").replace("どよう びは", "どようび わ")
    return seg

added = fixed = 0
new = {}
for jp, kana in want.items():
    old = rom.get(jp)
    seg = old if old is not None else segment(jp, kana)
    if old is None: added += 1
    seg2 = fix(jp, seg)
    if seg2 != seg: fixed += 1
    new[jp] = seg2
# точечные исправления разбора (частица слилась со словом, 歯 прочитан как частица)
MANUAL = {
    "猫は箱の中にいます。": "ねこ わ はこ の なか に います。",
    "寝る前に歯を磨きます。": "ねる まえ に は お みがきます。",
    "母がセーターをくれました。": "はは が せーたー お くれました。",
    "この歌を聞くたびに、故郷を思い出します。": "この うた お きく たび に、 ふるさと お おもいだします。",
    "明日、御社に伺います。": "あした、 おんしゃ に うかがいます。",
    "袋はいりません。": "ふくろ わ いりません。",
}
for jp, seg in MANUAL.items():
    if jp in new: new[jp] = seg
removed = len([k for k in rom if k not in new])
json.dump(new, open(D("romaji.json"), "w", encoding="utf8"), ensure_ascii=False, indent=0)
print(f"было {len(rom)}, стало {len(new)}: добавлено {added}, исправлено {fixed}, удалено устаревших {removed}")
