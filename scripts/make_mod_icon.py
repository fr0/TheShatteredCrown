# About/ModIcon.png: the mod's face in the mod list and on the loading
# screen's mod roll. RimWorld wants a small square - 64x64 reads well at
# every scale it is drawn at.
#
# Lorc's "crown" from game-icons.net (CC BY 3.0), the same pack and
# license as the ability icons - fetched white-on-black, recolored gold,
# set on the mod's dark rounded field, and given a jagged crack down the
# middle so it is THE SHATTERED crown and not just a crown. Built at 512
# and downsampled to 64 so the edges stay clean.
#
# Run:  py scripts\make_mod_icon.py
import io
import os
import urllib.request

from PIL import Image, ImageDraw

URL = "https://game-icons.net/icons/ffffff/000000/1x1/lorc/crown.png"
OUT = os.path.join(os.path.dirname(__file__), "..", "About", "ModIcon.png")

S = 512  # supersampled; saved at 64

FIELD = (26, 22, 30, 255)
GOLD = (214, 174, 82)


def main():
    raw = urllib.request.urlopen(URL, timeout=30).read()
    icon = Image.open(io.BytesIO(raw)).convert("RGBA").resize((S, S), Image.LANCZOS)

    # The download is white-on-black; read brightness as the glyph's alpha,
    # then pour gold through it.
    alpha = icon.convert("L")

    # The crack: a lightning-jag of transparency cut straight through the
    # glyph's alpha, slightly off-centre so the halves read unequal, the
    # way broken things do. Cut from the alpha rather than painted on top,
    # so the field colour shows through as absence, not as a drawn line.
    cut = ImageDraw.Draw(alpha)
    cut.polygon([(268, 60), (300, 175), (252, 260), (296, 360), (272, 460),
                 (252, 460), (270, 358), (226, 258), (274, 172), (246, 60)],
                fill=0)
    # A chip off the right edge of the break, so it reads as damage.
    cut.polygon([(300, 240), (344, 320), (300, 316)], fill=0)

    glyph = Image.new("RGBA", (S, S), GOLD + (255,))
    glyph.putalpha(alpha)

    img = Image.new("RGBA", (S, S), FIELD)
    img.alpha_composite(glyph)

    # Rounded field so the icon reads as a crest rather than a sticker.
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=90, fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(img, (0, 0), mask)

    out.resize((64, 64), Image.LANCZOS).save(OUT)
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
