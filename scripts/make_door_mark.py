# The king's door map marker: Lorc's crown (game-icons.net, CC BY 3.0,
# covered by the existing credit), gold with a dark outline so it reads
# against both the barrow floor and fogged rock. 64px, drawn in screen
# space by MapComponent_TSC_Barrow.
#
# Run:  py scripts\make_door_mark.py
import io
import os
import urllib.request

from PIL import Image, ImageFilter

URL = "https://game-icons.net/icons/ffffff/000000/1x1/lorc/crown.png"
OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "UI", "TSC_DoorMark.png")

GOLD = (232, 199, 112)
OUTLINE = (35, 28, 18)


def main():
    raw = urllib.request.urlopen(URL, timeout=30).read()
    src = Image.open(io.BytesIO(raw)).convert("L").resize((256, 256), Image.LANCZOS)
    glyph = Image.new("RGBA", src.size, GOLD + (255,))
    glyph.putalpha(src)
    # Outline: the glyph's alpha, dilated, in dark, underneath.
    halo = src.filter(ImageFilter.MaxFilter(9))
    outline = Image.new("RGBA", src.size, OUTLINE + (255,))
    outline.putalpha(halo)
    out = Image.alpha_composite(outline, glyph)
    out.resize((64, 64), Image.LANCZOS).save(OUT)
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
