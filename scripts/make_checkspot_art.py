"""Generate in-map textures for TSC check-spot buildings.

Flat-shaded sprites with dark outlines, drawn at 4x and downscaled, in the
same hand as the world-map icon generator. Output 128x128 RGBA into
Textures/Things/Building/.
"""
import os
import math
from PIL import Image, ImageDraw

SS = 4
SIZE = 128
CANVAS = SIZE * SS
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

OUTLINE = (44, 38, 32, 255)
STONE = (138, 134, 126, 255)
STONE_DARK = (108, 104, 97, 255)
MARBLE = (216, 212, 202, 255)
MARBLE_DARK = (184, 179, 168, 255)
RUNE = (72, 60, 48, 255)
EMBER = (214, 120, 52, 255)
DIRT = (122, 96, 66, 255)

def canvas():
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)

def W(f):
    return int(f * CANVAS)

LW = W(0.02)

def save(img, name):
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    img.save(path)
    print("wrote", path)


# ------------------------------------------------------------- graven stone
def make_graven_stone():
    img, d = canvas()
    # ground shadow
    d.ellipse([W(0.18), W(0.74), W(0.82), W(0.92)], fill=(0, 0, 0, 60))
    # standing slab, rounded top, slightly tapered
    d.polygon([(W(0.30), W(0.82)), (W(0.33), W(0.30)), (W(0.40), W(0.18)),
               (W(0.60), W(0.18)), (W(0.67), W(0.30)), (W(0.70), W(0.82))],
              fill=STONE, outline=OUTLINE, width=LW)
    # left face shading
    d.polygon([(W(0.30), W(0.82)), (W(0.33), W(0.30)), (W(0.40), W(0.18)),
               (W(0.44), W(0.18)), (W(0.40), W(0.30)), (W(0.38), W(0.82))],
              fill=STONE_DARK)
    # road-script: stacked carved lines with tick marks
    for i, y in enumerate((0.32, 0.42, 0.52, 0.62)):
        d.line([(W(0.42), W(y)), (W(0.62), W(y))], fill=RUNE, width=LW)
        for tx in (0.46, 0.52, 0.58):
            d.line([(W(tx), W(y - 0.03)), (W(tx), W(y))], fill=RUNE, width=int(LW * 0.8))
    save(img, "TSC_GravenStone")


# ------------------------------------------------------------- shrine altar
def make_shrine_altar():
    img, d = canvas()
    d.ellipse([W(0.14), W(0.70), W(0.86), W(0.94)], fill=(0, 0, 0, 60))
    # plinth
    d.polygon([(W(0.30), W(0.84)), (W(0.34), W(0.56), ), (W(0.66), W(0.56)), (W(0.70), W(0.84))],
              fill=MARBLE_DARK, outline=OUTLINE, width=LW)
    # basin bowl
    d.ellipse([W(0.16), W(0.34), W(0.84), W(0.62)], fill=MARBLE, outline=OUTLINE, width=LW)
    # bowl interior (dry, clean, faintly warm)
    d.ellipse([W(0.24), W(0.40), W(0.76), W(0.58)], fill=MARBLE_DARK, outline=OUTLINE, width=int(LW * 0.8))
    d.ellipse([W(0.34), W(0.45), W(0.66), W(0.55)], fill=(96, 84, 74, 255))
    # rim rite: carved marks around the front lip
    for i in range(7):
        a = math.pi * (0.15 + 0.7 * i / 6)
        x = 0.5 + 0.31 * math.cos(a)
        y = 0.52 + 0.11 * math.sin(a)
        d.line([(W(x), W(y)), (W(x), W(y + 0.04))], fill=RUNE, width=int(LW * 0.9))
    save(img, "TSC_ShrineAltar")


# ------------------------------------------------------------- beast tracks
def make_beast_tracks():
    img, d = canvas()
    # worn dirt patch
    d.ellipse([W(0.10), W(0.20), W(0.90), W(0.90)], fill=(122, 96, 66, 90))
    d.ellipse([W(0.18), W(0.28), W(0.82), W(0.82)], fill=(122, 96, 66, 60))

    def paw(cx, cy, s, ang):
        # pad
        d.ellipse([W(cx - 0.055 * s), W(cy - 0.045 * s), W(cx + 0.055 * s), W(cy + 0.055 * s)],
                  fill=DIRT, outline=OUTLINE, width=int(LW * 0.6))
        # toes
        for i in (-1.5, -0.5, 0.5, 1.5):
            a = ang + i * 0.42
            tx = cx + 0.085 * s * math.sin(a)
            ty = cy - 0.085 * s * math.cos(a)
            d.ellipse([W(tx - 0.022 * s), W(ty - 0.028 * s), W(tx + 0.022 * s), W(ty + 0.028 * s)],
                      fill=DIRT, outline=OUTLINE, width=int(LW * 0.5))

    paw(0.34, 0.68, 1.0, -0.2)
    paw(0.58, 0.52, 1.05, 0.1)
    paw(0.40, 0.36, 0.9, -0.1)
    paw(0.66, 0.24, 0.85, 0.15)
    save(img, "TSC_BeastTracks")


# ---------------------------------------------------------------- rune ward
def make_rune_ward():
    img, d = canvas()
    d.ellipse([W(0.16), W(0.72), W(0.84), W(0.94)], fill=(0, 0, 0, 60))
    # squat warding stone
    d.polygon([(W(0.26), W(0.84)), (W(0.28), W(0.34)), (W(0.38), W(0.22)),
               (W(0.62), W(0.22)), (W(0.72), W(0.34)), (W(0.74), W(0.84))],
              fill=STONE_DARK, outline=OUTLINE, width=LW)
    # the ward: ember sigil, a bound circle with a cross-stroke
    d.ellipse([W(0.37), W(0.34), W(0.63), W(0.60)], outline=EMBER, width=int(LW * 1.4))
    d.line([(W(0.50), W(0.28)), (W(0.50), W(0.66))], fill=EMBER, width=int(LW * 1.2))
    d.line([(W(0.40), W(0.52)), (W(0.60), W(0.42))], fill=EMBER, width=int(LW * 1.2))
    # heat shimmer ticks
    for x in (0.33, 0.50, 0.67):
        d.line([(W(x), W(0.72)), (W(x + 0.02), W(0.68))], fill=EMBER, width=int(LW * 0.8))
    save(img, "TSC_RuneWard")


# --------------------------------------------------------- collapsed passage
def make_collapsed_passage():
    img, d = canvas()
    d.ellipse([W(0.06), W(0.66), W(0.94), W(0.96)], fill=(0, 0, 0, 60))
    # boulder heap: big stones overlapping
    stones = [
        (0.14, 0.52, 0.48, 0.86, STONE_DARK),
        (0.40, 0.42, 0.78, 0.80, STONE),
        (0.62, 0.56, 0.92, 0.88, STONE_DARK),
        (0.26, 0.30, 0.60, 0.62, STONE),
        (0.06, 0.62, 0.32, 0.88, STONE),
        (0.52, 0.24, 0.80, 0.52, STONE_DARK),
    ]
    for x0, y0, x1, y1, fill in stones:
        d.ellipse([W(x0), W(y0), W(x1), W(y1)], fill=fill, outline=OUTLINE, width=LW)
    # dust/rubble bits
    for x, y in ((0.12, 0.90), (0.50, 0.92), (0.86, 0.92), (0.70, 0.88)):
        d.ellipse([W(x - 0.03), W(y - 0.02), W(x + 0.03), W(y + 0.02)],
                  fill=STONE_DARK, outline=OUTLINE, width=int(LW * 0.5))
    save(img, "TSC_CollapsedPassage")


if __name__ == "__main__":
    make_graven_stone()
    make_shrine_altar()
    make_beast_tracks()
    make_rune_ward()
    make_collapsed_passage()
