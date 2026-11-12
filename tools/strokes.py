import json, re, sys, os, urllib.request, concurrent.futures as cf
proj, cache = sys.argv[1], sys.argv[2]
chars = list(sys.argv[3]) if len(sys.argv) > 3 else []
for f in ("kanji.json", "kanji_n4.json", "kanji_n3.json", "kanji_n2.json"):
    chars += [k[0] for k in json.load(open(os.path.join(proj, "Data", f), encoding="utf8"))]
def fetch(c):
    name = "%05x.svg" % ord(c)
    p = os.path.join(cache, name)
    if not os.path.exists(p):
        url = "https://raw.githubusercontent.com/KanjiVG/kanjivg/master/kanji/" + name
        with urllib.request.urlopen(url, timeout=20) as r: data = r.read()
        open(p, "wb").write(data)
    svg = open(p, encoding="utf8").read()
    # Только группа StrokePaths, пути по порядку (id ...-s1, -s2, ...)
    m = re.search(r'<g id="kvg:StrokePaths_[^"]*"[^>]*>(.*?)</svg>', svg, re.S)
    body = m.group(1) if m else svg
    paths = re.findall(r'<path id="kvg:[0-9a-f]+-s(\d+)"[^>]*?\sd="([^"]+)"', body)
    paths.sort(key=lambda t: int(t[0]))
    return c, [d for _, d in paths]
# дополняем существующий файл (там же порядок черт каны), уже готовые знаки не качаем заново
dst = os.path.join(proj, "Data", "strokes.json")
out = json.load(open(dst, encoding="utf8")) if os.path.exists(dst) else {}
chars = [c for c in dict.fromkeys(chars) if c not in out]
missing = []
with cf.ThreadPoolExecutor(8) as ex:
    for fut in cf.as_completed([ex.submit(fetch, c) for c in chars]):
        try:
            c, ds = fut.result()
            if ds: out[c] = ds
            else: missing.append(c)
        except Exception as e: missing.append(str(e)[:60])
json.dump(out, open(dst, "w", encoding="utf8"), ensure_ascii=False, separators=(",", ":"))
print("ok", len(out), "of", len(chars), "missing:", missing[:10])
