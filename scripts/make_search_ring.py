# TSC_SearchRing: the ring a search mark draws on the ground.
#
# Drawn in screen space rather than on the map, because Real Fog of War puts
# its own cover over everything the world layer can reach. White, so the
# game can tint it gold or red at draw time; soft inner and outer edges so it
# does not alias into a dotted circle when the camera is zoomed out.
#
# Run:  py scripts\make_search_ring.py
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "UI")

S = 1024  # supersampled; saved at 256
THICKNESS = 44
INSET = 40


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    box = [INSET, INSET, S - INSET, S - INSET]
    d.ellipse(box, outline=(255, 255, 255, 255), width=THICKNESS)
    # A second, fainter ring just inside it reads as a sweep rather than a
    # drawn circle, which suits "roughly in here" better than a hard border.
    inner = [INSET + 110, INSET + 110, S - INSET - 110, S - INSET - 110]
    d.ellipse(inner, outline=(255, 255, 255, 90), width=14)
    img = img.filter(ImageFilter.GaussianBlur(5))
    path = os.path.join(OUT, "TSC_SearchRing.png")
    img.resize((256, 256), Image.LANCZOS).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
