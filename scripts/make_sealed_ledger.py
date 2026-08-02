# TSC_SealedLedger: the quiet contract's objective. A dark leather ledger,
# strapped shut, with the guild's red wax seal over the strap - the thing
# the party is paid to walk OUT with, drawn to read against ruin floors.
#
# Run:  py scripts\make_sealed_ledger.py
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Item")

S = 512  # supersampled; saved at 128

LEATHER = (86, 62, 42)
LEATHER_DARK = (58, 40, 26)
LEATHER_EDGE = (40, 27, 17)
PAGES = (214, 198, 162)
STRAP = (120, 88, 52)
WAX = (150, 38, 34)
WAX_LIGHT = (186, 60, 52)


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([96, 128, 436, 420], radius=22, fill=(0, 0, 0, 130))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(9)))

    # The block of pages, showing at the fore-edge.
    d.rounded_rectangle([104, 132, 424, 404], radius=14, fill=PAGES)
    for y in range(150, 396, 14):
        d.line([(410, y), (422, y)], fill=(178, 160, 124), width=3)

    # The cover, slightly proud of the pages.
    d.rounded_rectangle([88, 120, 408, 396], radius=18, fill=LEATHER_EDGE)
    d.rounded_rectangle([96, 128, 400, 388], radius=14, fill=LEATHER)
    d.rounded_rectangle([116, 148, 380, 368], radius=10, outline=LEATHER_DARK, width=6)
    # Spine bands.
    for y in (150, 250, 350):
        d.line([(88, y), (104, y)], fill=LEATHER_DARK, width=10)

    # The strap, buckled shut across the middle.
    d.rectangle([70, 236, 434, 292], fill=(70, 50, 32))
    d.rectangle([70, 244, 434, 284], fill=STRAP)
    d.line([(70, 264), (434, 264)], fill=(96, 68, 40), width=4)

    # The seal, over the strap: breaking one breaks the other.
    cx, cy, r = 248, 264, 62
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=WAX)
    d.ellipse([cx - r + 9, cy - r + 7, cx + r - 11, cy + r - 15], fill=WAX_LIGHT)
    # The guild's mark: the rider's road, as on the strongbox seal.
    d.line([cx - 26, cy + 16, cx + 26, cy - 18], fill=(96, 20, 18), width=9)
    d.line([cx - 26, cy - 8, cx + 6, cy - 8], fill=(96, 20, 18), width=7)
    d.line([cx - 2, cy + 20, cx + 26, cy + 20], fill=(96, 20, 18), width=7)

    path = os.path.join(OUT, "TSC_SealedLedger.png")
    img.resize((128, 128), Image.LANCZOS).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
