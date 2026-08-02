# TSC_Search: Lorc's "magnifying glass" from game-icons.net (CC BY 3.0),
# the same pack and license as the ability icons - fetched white-on-black
# and converted to the house format (white glyph, luminance alpha, 128px).
# Covered by the existing Lorc credit in About.xml and the README.
#
# Run:  py scripts\fetch_search_icon.py
import io
import os
import urllib.request

from PIL import Image

URL = "https://game-icons.net/icons/ffffff/000000/1x1/lorc/magnifying-glass.png"
OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "UI", "TSC_Search.png")


def main():
    raw = urllib.request.urlopen(URL, timeout=30).read()
    src = Image.open(io.BytesIO(raw)).convert("L")
    out = Image.new("RGBA", src.size, (255, 255, 255, 0))
    out.putalpha(src)  # luminance is the glyph; black background falls away
    out.resize((128, 128), Image.LANCZOS).save(OUT)
    print("wrote", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
