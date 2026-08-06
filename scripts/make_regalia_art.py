# The first king's regalia: ring, amulet, staff - the three things that
# power his undeath, fetched in Act 5 to wake him. 128x128 item art in the
# mod's gold-on-transparent style, supersampled 4x for clean edges.
#
# Run:  py scripts\make_regalia_art.py
import os

from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Item")

S = 512  # working size; saved at 128

GOLD = (214, 174, 82, 255)
GOLD_DARK = (150, 116, 46, 255)
GOLD_DEEP = (110, 84, 32, 255)
GEM = (150, 38, 34, 255)
GEM_LIGHT = (196, 74, 66, 255)
WOOD = (94, 66, 42, 255)
WOOD_DARK = (62, 43, 27, 255)
OUTLINE = (38, 30, 22, 255)


def canvas():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def save(img, name):
    img.resize((128, 128), Image.LANCZOS).save(os.path.join(OUT_DIR, name))
    print("wrote", name)


def ring():
    img, d = canvas()
    # The band, tilted into a fat ellipse so it reads as a ring and not a coin.
    d.ellipse([96, 156, 416, 420], fill=OUTLINE)
    d.ellipse([108, 168, 404, 408], fill=GOLD)
    d.ellipse([156, 216, 356, 372], fill=OUTLINE)
    d.ellipse([168, 228, 344, 360], fill=(0, 0, 0, 0))
    # Shadowed inner-lower rim for depth.
    d.arc([120, 180, 392, 396], start=20, end=160, fill=GOLD_DARK, width=26)
    # The bezel and its dark stone.
    d.ellipse([206, 96, 306, 196], fill=OUTLINE)
    d.ellipse([216, 106, 296, 186], fill=GOLD_DARK)
    d.ellipse([228, 118, 284, 174], fill=GEM)
    d.ellipse([240, 128, 262, 148], fill=GEM_LIGHT)
    save(img, "TSC_KingsRing.png")


def amulet():
    img, d = canvas()
    # Chain: two arcs meeting at the bail.
    d.arc([96, 60, 416, 380], start=200, end=270, fill=OUTLINE, width=30)
    d.arc([96, 60, 416, 380], start=270, end=340, fill=OUTLINE, width=30)
    d.arc([104, 68, 408, 372], start=202, end=338, fill=GOLD_DARK, width=16)
    # Bail.
    d.ellipse([226, 158, 286, 218], fill=OUTLINE)
    d.ellipse([238, 170, 274, 206], fill=GOLD)
    d.ellipse([248, 180, 264, 196], fill=(0, 0, 0, 0))
    # The medallion.
    d.ellipse([132, 212, 380, 460], fill=OUTLINE)
    d.ellipse([144, 224, 368, 448], fill=GOLD)
    d.ellipse([168, 248, 344, 424], fill=GOLD_DARK)
    # Sunburst: the king's mark.
    cx, cy = 256, 336
    for i in range(8):
        import math
        a = math.pi * 2 * i / 8
        x1 = cx + 34 * math.cos(a)
        y1 = cy + 34 * math.sin(a)
        x2 = cx + 74 * math.cos(a)
        y2 = cy + 74 * math.sin(a)
        d.line([x1, y1, x2, y2], fill=GOLD, width=14)
    d.ellipse([cx - 30, cy - 30, cx + 30, cy + 30], fill=GOLD)
    d.ellipse([cx - 18, cy - 18, cx + 18, cy + 18], fill=GEM)
    save(img, "TSC_KingsAmulet.png")


def staff():
    img, d = canvas()
    # The shaft, corner to corner.
    d.line([120, 470, 350, 130], fill=OUTLINE, width=44)
    d.line([124, 462, 346, 136], fill=WOOD, width=28)
    d.line([136, 448, 330, 160], fill=WOOD_DARK, width=8)
    # Gold collar under the head.
    d.line([322, 172, 356, 122], fill=OUTLINE, width=56)
    d.line([325, 168, 352, 127], fill=GOLD_DARK, width=40)
    # The head: an open crescent holding a stone - the shape the crown
    # shards want to be, echoed in the tool that made them.
    d.ellipse([300, 28, 452, 180], fill=OUTLINE)
    d.ellipse([318, 46, 434, 162], fill=(0, 0, 0, 0))
    d.arc([312, 40, 440, 168], start=100, end=340, fill=GOLD, width=26)
    d.ellipse([352, 78, 404, 130], fill=OUTLINE)
    d.ellipse([360, 86, 396, 122], fill=GEM)
    d.ellipse([366, 92, 380, 106], fill=GEM_LIGHT)
    save(img, "TSC_KingsStaff.png")


if __name__ == "__main__":
    ring()
    amulet()
    staff()
