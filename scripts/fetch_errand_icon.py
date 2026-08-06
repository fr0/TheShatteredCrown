# TSC_ErrandHolds: Lorc's winged foot from game-icons.net (CC BY 3.0),
# the same pack and license as the other ability icons - fetched
# white-on-black and converted to the house format (white glyph,
# luminance alpha, 128px). Covered by the existing Lorc credit.
#
# Run:  py scripts\fetch_errand_icon.py
import io
import os
import urllib.request

from PIL import Image

# First name that resolves wins; the pack has renamed icons before.
CANDIDATES = [
    "https://game-icons.net/icons/ffffff/000000/1x1/lorc/wingfoot.png",
    "https://game-icons.net/icons/ffffff/000000/1x1/lorc/winged-shoe.png",
    "https://game-icons.net/icons/ffffff/000000/1x1/lorc/walking-boot.png",
]
OUT = os.path.join(os.path.dirname(__file__), "..",
                   "Textures", "UI", "TSC_Abilities", "TSC_ErrandHolds.png")


def main():
    raw = None
    for url in CANDIDATES:
        try:
            raw = urllib.request.urlopen(url, timeout=30).read()
            print("fetched", url.rsplit("/", 1)[-1])
            break
        except Exception:
            continue
    if raw is None:
        raise SystemExit("no candidate icon URL resolved")
    src = Image.open(io.BytesIO(raw)).convert("L")
    # Crown-gold, matched to the other shard powers rather than guessed:
    # sample the King's Mercy icon's mean opaque color so the whole shard
    # family reads as one set on the gizmo bar.
    gold = (214, 174, 82)
    mercy_path = os.path.join(os.path.dirname(OUT), "TSC_KingsMercy.png")
    if os.path.exists(mercy_path):
        mercy = Image.open(mercy_path).convert("RGBA")
        totals, count = [0, 0, 0], 0
        for r, g, b, a in mercy.getdata():
            if a > 128:
                totals[0] += r
                totals[1] += g
                totals[2] += b
                count += 1
        if count > 0:
            gold = tuple(c // count for c in totals)
    out = Image.new("RGBA", src.size, gold + (255,))
    out.putalpha(src)  # luminance is the glyph; black background falls away
    out.resize((128, 128), Image.LANCZOS).save(OUT)
    print("wrote", os.path.normpath(OUT), "tinted", gold)


if __name__ == "__main__":
    main()
