# Иллюстрации: Microsoft Fluent Emoji (MIT), 3D-версия, уменьшение до 128 px → Assets/emoji/<ключ>.png
# python -X utf8 tools/get_emoji.py <проект>
import os, sys, io, urllib.request, urllib.parse, concurrent.futures as cf
from PIL import Image
proj = sys.argv[1]
out = os.path.join(proj, "Assets", "emoji"); os.makedirs(out, exist_ok=True)
BASE = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/main/assets/"
NAMES = ["Speech balloon", "House with garden", "Input numbers", "Alarm clock", "Bento box", "Cityscape", "Bullet train", "Backpack",
         "Cherry blossom", "Rocket", "Artist palette", "Red question mark", "Briefcase", "Couch and lamp", "World map", "Pill",
         "Smiling face with hearts", "Gear", "Link", "Name badge", "Hot beverage", "T-shirt", "Round pushpin", "Hotel", "Stethoscope",
         "Clapper board", "Mobile phone", "Musical note", "Sunrise", "Wrapped gift", "Shortcake", "Station", "SOS button", "Calendar",
         "Sun with face", "House", "Tokyo tower", "Books", "Cloud with rain", "Puzzle piece", "Stopwatch", "Writing hand", "Light bulb",
         "Trophy", "Fire", "Sparkles", "Graduation cap", "Japanese symbol for beginner", "Open book", "Speaking head", "Headphone",
         "Sushi", "Mount fuji", "Red paper lantern", "Shinto shrine", "Bookmark tabs", "Memo", "Dart", "Game die", "Video game",
         "Microphone", "Map of japan", "Department store", "Moon viewing ceremony", "Bamboo", "Rice ball", "Money bag", "Spiral calendar",
         "Keyboard", "Radio", "Scroll", "Thought balloon", "Party popper", "Teacup without handle", "Ear", "Eyes", "Post office",
         "Convenience store", "Taxi", "Hot springs", "Umbrella with rain drops", "Fireworks", "Office building", "School", "Chopsticks",
         "Tomato", "Man office worker", "Hourglass done", "Pen"]

def key(n): return n.lower().replace(" ", "_").replace("-", "")

def fetch(n):
    if os.path.exists(os.path.join(out, key(n) + ".png")): return n, True
    file = n.lower().replace(" ", "_") + "_3d.png"
    for path in (f"{n}/3D/{file}", f"{n}/Default/3D/{n.lower().replace(' ', '_')}_3d_default.png"):
        url = BASE + urllib.parse.quote(path)
        try:
            data = urllib.request.urlopen(url, timeout=20).read()
            im = Image.open(io.BytesIO(data)).convert("RGBA")
            im.thumbnail((128, 128), Image.LANCZOS)
            im.save(os.path.join(out, key(n) + ".png"), optimize=True)
            return n, True
        except Exception:
            pass
    return n, False

with cf.ThreadPoolExecutor(8) as ex:
    res = list(ex.map(fetch, NAMES))
missing = [n for n, ok in res if not ok]
total = sum(os.path.getsize(os.path.join(out, f)) for f in os.listdir(out))
print("ok", len(NAMES) - len(missing), "of", len(NAMES), "size KB", total // 1024, "missing:", missing)
