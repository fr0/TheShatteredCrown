# TSC_Search: the gizmo for reading the ground.
#
# An open eye over a boot print: perception and survival, the two things the
# roll is made of. Flat and high contrast so it survives at 24px in a gizmo
# strip.
#
# Run:  py scripts\make_search_icon.py
import os

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "UI")

S = 512  # supersampled; saved at 128

PALE = (222, 218, 206)
DARK = (58, 52, 44)
IRIS = (86, 132, 148)
TRACK = (150, 132, 104)


def eye(d):
    """A lens shape: two arcs meeting at the corners."""
    d.polygon([(70, 200), (256, 92), (442, 200), (256, 308)], fill=DARK)
    d.polygon([(96, 200), (256, 116), (416, 200), (256, 284)], fill=PALE)
    d.ellipse([206, 150, 306, 250], fill=IRIS)
    d.ellipse([228, 172, 272, 216], fill=DARK)
    d.ellipse([236, 180, 254, 198], fill=PALE)


def track(d):
    """A boot print below, angled, so the icon is not just an eye."""
    d.ellipse([176, 336, 268, 436], fill=TRACK)
    d.ellipse([258, 386, 318, 452], fill=TRACK)
    for i, x in enumerate((186, 216, 246)):
        d.ellipse([x, 316 + i * 6, x + 26, 348 + i * 6], fill=TRACK)


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    track(d)
    eye(d)
    path = os.path.join(OUT, "TSC_Search.png")
    img.resize((128, 128), Image.LANCZOS).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
