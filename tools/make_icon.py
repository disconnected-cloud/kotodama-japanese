# Иконка Kotodama (言霊): тёмный скруглённый квадрат, мягкое свечение, иероглиф 言 цвета сакуры и цветок.
import sys, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter
out = sys.argv[1]
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
# фон с вертикальным градиентом
bg = Image.new("RGBA", (S, S))
d = ImageDraw.Draw(bg)
for y in range(S):
    t = y / S
    r = int(0x24 * (1 - t) + 0x10 * t); g = int(0x15 * (1 - t) + 0x10 * t); b = int(0x22 * (1 - t) + 0x16 * t)
    d.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=230, fill=255)
img.paste(bg, (0, 0), mask)
# свечение
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([S * 0.18, S * 0.12, S * 0.86, S * 0.80], fill=(240, 122, 160, 70))
glow = glow.filter(ImageFilter.GaussianBlur(110))
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))
# иероглиф
font = None
for f in ("C:/Windows/Fonts/yumindb.ttf", "C:/Windows/Fonts/YuMinchoDemibold.ttf", "C:/Windows/Fonts/yumin.ttf", "C:/Windows/Fonts/msmincho.ttc", "C:/Windows/Fonts/YuGothB.ttc"):
    try: font = ImageFont.truetype(f, 640); break
    except Exception: pass
txt = Image.new("RGBA", (S, S), (0, 0, 0, 0))
td = ImageDraw.Draw(txt)
bbox = td.textbbox((0, 0), "言", font=font)
w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
td.text(((S - w) / 2 - bbox[0], (S - h) / 2 - bbox[1] + 10), "言", font=font, fill=(246, 132, 168, 255))
img = Image.alpha_composite(img, txt)
# цветок сакуры из пяти лепестков с выемкой
flower = Image.new("RGBA", (S, S), (0, 0, 0, 0))
cx, cy = S * 0.80, S * 0.21
for k in range(5):
    petal = Image.new("RGBA", (240, 240), (0, 0, 0, 0))
    pd = ImageDraw.Draw(petal)
    pd.ellipse([95, 18, 145, 120], fill=(255, 196, 214, 240))
    petal = petal.rotate(72 * k, center=(120, 120), resample=Image.BICUBIC)
    flower.alpha_composite(petal, (int(cx - 120), int(cy - 120)))
ImageDraw.Draw(flower).ellipse([cx - 13, cy - 13, cx + 13, cy + 13], fill=(240, 122, 160, 255))
img = Image.alpha_composite(img, flower)
img.save(out.replace(".ico", ".png"))
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save(out, sizes=sizes)
print("ok", out)
