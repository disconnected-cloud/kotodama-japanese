# Питч-акцент (Токио) из Kanjium (CC BY-SA 4.0): только слова, которые есть в курсе и словаре.
# python -X utf8 tools/build_accents.py <проект> <accents.txt>
import gzip, json, os, sys
proj, src = sys.argv[1], sys.argv[2]
D = lambda f: os.path.join(proj, "Data", f)
forms = set()
for line in gzip.open(D("dict.tsv.gz"), "rt", encoding="utf8"):
    p = line.rstrip("\n").split("\t")
    if len(p) >= 3:
        forms.update(x for x in p[1].split("、") if x); forms.add(p[2])
for f in ("vocab.json", "vocab_n4.json", "vocab_plus.json", "vocab_n3.json"):
    for topic in json.load(open(D(f), encoding="utf8")).values():
        for w in topic: forms.add(w[0]); forms.add(w[1] or w[0])
out = []
for line in open(src, encoding="utf8"):
    p = line.rstrip("\n").split("\t")
    if len(p) >= 3 and p[0] in forms:
        acc = p[2].split(",")[0].strip()
        if acc.isdigit(): out.append(f"{p[0]}\t{p[1]}\t{acc}")
with gzip.open(D("accents.tsv.gz"), "wt", encoding="utf8", compresslevel=9) as f: f.write("\n".join(out))
print("accents:", len(out), "size KB:", os.path.getsize(D("accents.tsv.gz")) // 1024)
