"""
Упаковка Linux-сборки в Kotodama-linux-x64.tar.gz с правильными правами (можно запускать и в Windows).

  python tools/pack_linux.py release/Kotodama-linux release/Kotodama-linux-x64.tar.gz
"""
import sys, tarfile, pathlib, io

src = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
pkg = pathlib.Path(__file__).resolve().parent.parent / "packaging" / "linux"
EXEC = {"Kotodama", "install.sh", "uninstall.sh"}

def add(tar, data: bytes, name: str, mode: int):
    ti = tarfile.TarInfo("Kotodama/" + name)
    ti.size, ti.mode, ti.mtime = len(data), mode, 0
    tar.addfile(ti, io.BytesIO(data))

with tarfile.open(out, "w:gz", compresslevel=9) as tar:
    for f in sorted(src.rglob("*")):
        if f.is_file():
            rel = f.relative_to(src).as_posix()
            if rel.endswith(".pdb") or rel == "ggml-metal.metal" or (rel.startswith("runtimes/") and not rel.startswith("runtimes/linux-x64/")):
                continue
            add(tar, f.read_bytes(), rel, 0o755 if rel in EXEC or rel.endswith(".so") else 0o644)
    for f in sorted(pkg.iterdir()):
        data = f.read_bytes()
        if f.suffix in (".sh", ".desktop", ".txt"):
            data = data.replace(b"\r\n", b"\n")          # скриптам Linux нужны LF
        add(tar, data, f.name, 0o755 if f.name in EXEC else 0o644)
print(f"{out}: {out.stat().st_size / 1e6:.1f} МБ")
