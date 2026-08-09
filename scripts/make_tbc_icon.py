# ModIcon for the standalone Turn-Based Combat mod: Lorc's "hourglass"
# (game-icons.net, CC BY 3.0 - same pack and license as the rest of the
# mod's glyph art) poured gold, set inside a thin ring (the turn cycle),
# on the same dark rounded field as The Shattered Crown's icon - visibly
# the same family, visibly not the crown.
#
# Written to docs/standalone/ (the persistent asset home that
# build_standalone.py copies About assets from - dist/ is wiped on every
# build) and installed into dist/ directly when one exists.
#
# Run:  py scripts\make_tbc_icon.py
import io
import os
import urllib.request

from PIL import Image, ImageDraw

URL = "https://game-icons.net/icons/ffffff/000000/1x1/lorc/hourglass.png"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "docs", "standalone")

S = 512  # supersampled; saved at 64

FIELD = (26, 22, 30, 255)
GOLD = (214, 174, 82)
GOLD_DIM = (150, 122, 62)


def main():
    raw = urllib.request.urlopen(URL, timeout=30).read()
    icon = Image.open(io.BytesIO(raw)).convert("RGBA")
    # Inset the glyph so the ring has room around it.
    glyph_s = int(S * 0.62)
    icon = icon.resize((glyph_s, glyph_s), Image.LANCZOS)
    alpha = icon.convert("L")

    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([8, 8, S - 8, S - 8], radius=96, fill=FIELD)

    # The turn ring: a thin circle broken at four compass points, so it
    # reads as segments (turns) rather than a border.
    box = [64, 64, S - 64, S - 64]
    for a0 in (12, 102, 192, 282):
        d.arc(box, a0, a0 + 66, fill=GOLD_DIM, width=14)

    gold = Image.new("RGBA", (glyph_s, glyph_s), GOLD + (255,))
    gold.putalpha(alpha)
    off = (S - glyph_s) // 2
    img.alpha_composite(gold, (off, off))

    img = img.resize((64, 64), Image.LANCZOS)
    os.makedirs(ASSETS, exist_ok=True)
    out = os.path.join(ASSETS, "ModIcon.png")
    img.save(out)
    print("wrote", os.path.relpath(out, ROOT))
    dist_about = os.path.join(ROOT, "dist", "TurnBasedCombat", "About")
    if os.path.isdir(dist_about):
        img.save(os.path.join(dist_about, "ModIcon.png"))
        print("installed into dist/TurnBasedCombat/About/")


if __name__ == "__main__":
    main()
