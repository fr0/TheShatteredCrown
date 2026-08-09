# About/Preview.png for the standalone Turn-Based Combat mod (640x360, the
# Steam tile). Georgia Bold wordmark - the same face as every Shattered
# Crown promo image - over a dark field with the mod's own spell-ring
# texture ghosted behind it in ember orange, so the two Workshop pages
# read as the same family.
#
# Written to docs/standalone/ (the persistent asset home build_standalone.py
# copies About assets from) and installed into dist/ when one exists.
#
# Run:  py scripts\make_tbc_preview.py
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "docs", "standalone")
RING = os.path.join(ROOT, "Textures", "Things", "Mote", "TSC_SpellRing.png")

W, H = 640, 360
GOLD = (214, 178, 102)
DIM = (150, 124, 74)
RULE = (90, 74, 44)
FIELD = (36, 30, 22)
EMBER = (214, 120, 52)


def main():
    rgba = Image.new("RGBA", (W, H), FIELD + (255,))

    # ghosted turn-rings behind the wordmark
    ring = Image.open(RING).resize((520, 520))
    tint = Image.new("RGBA", ring.size, EMBER + (60,))
    tint.putalpha(ring.split()[3].point(lambda a: a // 4))
    rgba.alpha_composite(tint, (60, -80))
    rgba.alpha_composite(tint, (200, 40))

    d = ImageDraw.Draw(rgba)
    f1 = ImageFont.truetype(r"C:\Windows\Fonts\georgiab.ttf", 58)
    f2 = ImageFont.truetype(r"C:\Windows\Fonts\georgiab.ttf", 24)
    for i, line in enumerate(["TURN-BASED", "COMBAT"]):
        w = d.textlength(line, font=f1)
        x, y = (W - w) / 2, 105 + i * 70
        d.text((x + 2, y + 2), line, font=f1, fill=(0, 0, 0))
        d.text((x, y), line, font=f1, fill=GOLD)
    d.line([(W / 2 - 180, 258), (W / 2 + 180, 258)], fill=RULE, width=2)
    sub = "initiative - action points - turn order"
    w = d.textlength(sub, font=f2)
    d.text(((W - w) / 2, 268), sub, font=f2, fill=DIM)

    os.makedirs(ASSETS, exist_ok=True)
    out = os.path.join(ASSETS, "Preview.png")
    rgba.convert("RGB").save(out)
    print("wrote", os.path.relpath(out, ROOT))
    dist_about = os.path.join(ROOT, "dist", "TurnBasedCombat", "About")
    if os.path.isdir(dist_about):
        rgba.convert("RGB").save(os.path.join(dist_about, "Preview.png"))
        print("installed into dist/TurnBasedCombat/About/")


if __name__ == "__main__":
    main()
