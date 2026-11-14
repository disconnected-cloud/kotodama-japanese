"""
Сборка учебных данных одной командой (Е5): скачивание закреплённых источников с проверкой SHA-256 (tools/sources.json)
и запуск скриптов в правильном порядке. Работает в Windows и Linux.

    python -m venv .venv
    .venv/bin/pip install -r tools/requirements.txt        (Windows: .venv\\Scripts\\pip ...)
    .venv/bin/python -X utf8 tools/build_all.py . кэш      [шаги через запятую, по умолчанию все безопасные]

Шаги (по порядку): dict (словарь и латиница), accents, mnemonics, strokes, texts, romaji, glosses.
Шаг words (новые слова из Tatoeba) меняет состав курса — запускается только явно: build_all.py . кэш words
"""
import hashlib, json, os, subprocess, sys, tarfile, urllib.request
from pathlib import Path

proj, cache = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
steps = sys.argv[3].split(",") if len(sys.argv) > 3 else ["dict", "accents", "mnemonics", "strokes", "texts", "romaji", "glosses"]
src = json.load(open(proj / "tools" / "sources.json", encoding="utf8"))
cache.mkdir(parents=True, exist_ok=True)
py = [sys.executable, "-X", "utf8"]


def fetch(key):
    s = src[key]
    name = urllib.request.url2pathname(s["url"].rsplit("/", 1)[1]).replace("+", "_")
    path = cache / name
    if not path.exists():
        print(f"скачиваю {key}…")
        urllib.request.urlretrieve(s["url"], path)
    h = hashlib.sha256(path.read_bytes()).hexdigest()
    if s.get("sha256") and h != s["sha256"]:
        path.unlink()
        sys.exit(f"{key}: контрольная сумма не совпала ({h}) — источник изменился, проверь и обнови tools/sources.json")
    if not s.get("sha256"):
        print(f"  {key}: хэш не закреплён, скачанный файл — {h}")
    return path


def untar_json(path):
    out = path.with_suffix("")
    if not out.exists():
        with tarfile.open(path) as t:
            member = next(m for m in t.getmembers() if m.name.endswith(".json"))
            out.write_bytes(t.extractfile(member).read())
    return out


def run(script, *args):
    print(f"→ {script} {' '.join(map(str, args))}")
    subprocess.run(py + [str(proj / "tools" / script), str(proj), *map(str, args)], check=True)


for step in steps:
    if step == "dict": run("build_data.py", untar_json(fetch("jmdict-rus")))
    elif step == "accents": run("build_accents.py", fetch("kanjium-accents"))
    elif step == "mnemonics": run("build_mnemonics.py", fetch("kradfile"))
    elif step == "strokes": run("strokes.py", cache / "kanjivg")
    elif step == "texts": run("build_texts.py")
    elif step == "romaji": run("update_romaji.py")
    elif step == "glosses": run("clean_glosses.py"); run("fix_examples.py"); run("fix_n1.py")
    elif step == "words": run("build_words.py", cache / "tatoeba", untar_json(fetch("jmdict-rus")))
    else: sys.exit(f"неизвестный шаг {step}")
print("готово — проверь изменения в Data/ и прогони тесты: dotnet test tests/Kotodama.XTests")
