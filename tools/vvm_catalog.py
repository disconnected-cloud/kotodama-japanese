# Каталог голосов VOICEVOX: читает только metas.json из каждого .vvm (zip) через HTTP Range — без скачивания пакетов целиком.
import io, json, sys, zipfile, urllib.request, concurrent.futures as cf
REL = "https://github.com/VOICEVOX/voicevox_vvm/releases/download/0.17.1/"
NAMES = [str(i) for i in range(26)] + ["n0", "s0"]

class HttpFile(io.RawIOBase):
    def __init__(self, url):
        self.url = urllib.request.urlopen(urllib.request.Request(url, method="HEAD")).url  # следуем редиректу
        req = urllib.request.Request(self.url, method="HEAD")
        self.size = int(urllib.request.urlopen(req).headers["Content-Length"]); self.pos = 0
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self, off, whence=0):
        self.pos = off if whence == 0 else self.pos + off if whence == 1 else self.size + off
        return self.pos
    def read(self, n=-1):
        if n < 0: n = self.size - self.pos
        if n == 0 or self.pos >= self.size: return b""
        end = min(self.size, self.pos + n) - 1
        req = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{end}"})
        data = urllib.request.urlopen(req).read()
        self.pos += len(data); return data
    def readinto(self, b):
        d = self.read(len(b)); b[:len(d)] = d; return len(d)

def info(name):
    f = HttpFile(REL + name + ".vvm")
    z = zipfile.ZipFile(io.BufferedReader(f, 1 << 16))
    metas = json.loads(z.read("metas.json").decode("utf8"))
    return name, f.size, [{"name": m["name"], "styles": [{"name": s["name"], "id": s["id"]} for s in m["styles"] if s.get("type", "talk") in ("talk", "streaming_talk")]} for m in metas]

out = []
with cf.ThreadPoolExecutor(6) as ex:
    for name, size, chars in ex.map(info, NAMES):
        chars = [c for c in chars if c["styles"]]
        if chars: out.append({"file": name + ".vvm", "size": size, "chars": chars})
json.dump(out, open(sys.argv[1], "w", encoding="utf8"), ensure_ascii=False, indent=1)
for p in out: print(p["file"], p["size"] // 1_000_000, "MB:", ", ".join(c["name"] + f"({len(c['styles'])})" for c in p["chars"]))
